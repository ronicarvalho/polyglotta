using Microsoft.Extensions.Logging;
using Polyglotta.Core.Audio;
using Polyglotta.Core.Translation;

namespace Polyglotta.Core.Pipeline;

/// <summary>
/// One translation direction: capture → translator → render.
/// The app runs two instances: outbound (mic → virtual mic) and
/// inbound (system loopback → headphones).
/// </summary>
public sealed class DubbingPipeline : IAsyncDisposable
{
    private readonly IAudioCaptureDevice _capture;
    private readonly ISpeechTranslator _translator;
    private readonly IAudioRenderDevice _render;
    private readonly ILogger _log;
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;
    private DateTimeOffset _lastSpeechAt = DateTimeOffset.MinValue;

    public string Name { get; }
    public bool Muted { get; set; }

    public DubbingPipeline(
        string name,
        IAudioCaptureDevice capture,
        ISpeechTranslator translator,
        IAudioRenderDevice render,
        ILogger<DubbingPipeline> log)
    {
        Name = name;
        _capture = capture;
        _translator = translator;
        _render = render;
        _log = log;
    }

    public async Task StartAsync(TranslatorOptions options, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _translator.Transcript += (_, e) =>
            _log.LogInformation("[{Pipeline}] {Kind}: \"{Src}\" -> \"{Dst}\"",
                Name, e.IsFinal ? "final" : "partial", e.SourceText, e.TranslatedText);

        _translator.SynthesizedAudio += (_, audio) =>
        {
            var latency = DateTimeOffset.UtcNow - _lastSpeechAt;
            _log.LogDebug("[{Pipeline}] synthesized {Bytes} bytes (~{Latency:F1}s after speech)",
                Name, audio.Length, latency.TotalSeconds);
            _render.Enqueue(audio);
        };

        _translator.Faulted += (_, ex) =>
            _log.LogError(ex, "[{Pipeline}] translator faulted; RF-07 reconnection not yet implemented", Name);

        await _translator.StartAsync(options, _cts.Token);
        await _render.StartAsync(_cts.Token);
        await _capture.StartAsync(_cts.Token);

        _pumpTask = PumpAsync(_cts.Token);
        _log.LogInformation("[{Pipeline}] started ({Src} -> {Dst})", Name, options.SourceLanguage, options.TargetLanguage);
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        await foreach (var frame in _capture.Frames.ReadAllAsync(ct))
        {
            if (Muted) continue;
            _lastSpeechAt = frame.CapturedAt;
            _translator.PushAudio(frame.Pcm);
        }
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_pumpTask is not null)
        {
            try { await _pumpTask; } catch (OperationCanceledException) { }
        }
        await _capture.StopAsync();
        await _translator.StopAsync();
        await _render.StopAsync();
        _log.LogInformation("[{Pipeline}] stopped", Name);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _capture.DisposeAsync();
        await _translator.DisposeAsync();
        await _render.DisposeAsync();
        _cts?.Dispose();
    }
}
