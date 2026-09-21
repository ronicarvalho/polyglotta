using Microsoft.Extensions.Logging;
using Polyglotta.Core.Audio;
using Polyglotta.Core.Translation;

namespace Polyglotta.Core.Pipeline;

/// <summary>
/// One translation direction: capture → translator → render.
/// The app runs two instances: outbound (mic → virtual mic) and
/// inbound (system loopback → headphones).
/// <para>
/// Lifecycle: <see cref="StartAsync"/> / <see cref="StopAsync"/> may be called repeatedly
/// (stop is idempotent); <see cref="DisposeAsync"/> stops and releases the stages.
/// </para>
/// </summary>
public sealed class DubbingPipeline : IAsyncDisposable
{
    private readonly IAudioCaptureDevice _capture;
    private readonly ISpeechTranslator _translator;
    private readonly IAudioRenderDevice _render;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _pumpTask;
    private bool _disposed;

    public string Name { get; }
    public bool Muted { get; set; }
    public bool IsRunning => _pumpTask is { IsCompleted: false };

    /// <summary>End-of-phrase → first synthesized audio (PRD-001 RNF latência).</summary>
    public LatencyTracker Latency { get; } = new();

    /// <summary>
    /// Raised when a stage fails while running (translator error, capture device gone).
    /// The pipeline does not recover by itself — RF-07 reconnection lives above this class.
    /// </summary>
    public event EventHandler<Exception>? Faulted;

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

        // Subscribed once for the pipeline's lifetime; Start/Stop never touch these.
        _translator.Transcript += OnTranscript;
        _translator.SynthesizedAudio += OnSynthesizedAudio;
        _translator.Faulted += OnTranslatorFaulted;
    }

    public async Task StartAsync(TranslatorOptions options, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRunning)
                throw new InvalidOperationException($"Pipeline '{Name}' is already running.");

            _cts = new CancellationTokenSource();
            await _translator.StartAsync(options, ct);
            await _render.StartAsync(ct);
            await _capture.StartAsync(ct);

            _pumpTask = PumpAsync(_cts.Token);
            _log.LogInformation("[{Pipeline}] started ({Src} -> {Dst})", Name, options.SourceLanguage, options.TargetLanguage);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _capture.Frames.ReadAllAsync(ct))
            {
                if (Muted) continue;
                Latency.OnFrame(frame);
                _translator.PushAudio(frame.Pcm);
            }
            // Reader completed without error: capture stopped normally.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[{Pipeline}] capture faulted", Name);
            Faulted?.Invoke(this, ex);
        }
    }

    /// <summary>Stops all stages. Safe to call more than once; every stage is attempted even if one fails.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_cts is null)
                return; // never started, or already stopped

            _cts.Cancel();
            if (_pumpTask is not null)
                await _pumpTask; // PumpAsync swallows its own exceptions
            _pumpTask = null;
            _cts.Dispose();
            _cts = null;

            var errors = new List<Exception>();
            await TryStopAsync(() => _capture.StopAsync(ct), "capture", errors);
            await TryStopAsync(() => _translator.StopAsync(ct), "translator", errors);
            await TryStopAsync(() => _render.StopAsync(ct), "render", errors);

            _log.LogInformation("[{Pipeline}] stopped", Name);
            if (errors.Count > 0)
                throw new AggregateException($"Pipeline '{Name}' stopped with errors.", errors);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task TryStopAsync(Func<Task> stop, string stage, List<Exception> errors)
    {
        try
        {
            await stop();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[{Pipeline}] {Stage} failed to stop", Name, stage);
            errors.Add(ex);
        }
    }

    private void OnTranscript(object? _, TranscriptEvent e)
    {
        _log.LogInformation("[{Pipeline}] {Kind}: \"{Src}\" -> \"{Dst}\"",
            Name, e.IsFinal ? "final" : "partial", e.SourceText, e.TranslatedText);
        if (e.IsFinal)
            Latency.OnFinalTranscript();
    }

    private void OnSynthesizedAudio(object? _, ReadOnlyMemory<byte> audio)
    {
        if (Latency.OnSynthesizedAudio(DateTimeOffset.UtcNow) is { } latency)
            _log.LogInformation("[{Pipeline}] latency {Latency:F2}s (p95 {P95:F2}s, n={Count})",
                Name, latency.TotalSeconds, Latency.P95!.Value.TotalSeconds, Latency.Count);
        _render.Enqueue(audio);
    }

    private void OnTranslatorFaulted(object? _, Exception ex)
    {
        _log.LogError(ex, "[{Pipeline}] translator faulted", Name);
        Faulted?.Invoke(this, ex);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            await StopAsync();
        }
        catch (AggregateException ex)
        {
            _log.LogWarning(ex, "[{Pipeline}] errors while stopping during dispose", Name);
        }

        _disposed = true;
        _translator.Transcript -= OnTranscript;
        _translator.SynthesizedAudio -= OnSynthesizedAudio;
        _translator.Faulted -= OnTranslatorFaulted;

        await _capture.DisposeAsync();
        await _translator.DisposeAsync();
        await _render.DisposeAsync();
        _lifecycle.Dispose();
    }
}
