using System.Threading.Channels;
using Google.GenAI;
using Google.GenAI.Types;
using Polyglotta.Core.Audio;
using Polyglotta.Core.Configuration;
using Polyglotta.Core.Translation;

namespace Polyglotta.Translation.Gemini;

/// <summary>
/// <see cref="ISpeechTranslator"/> sobre a Gemini Live API em modo de tradução (ADR-0006).
/// <para>
/// Fala-para-fala nativo: o áudio canônico (PCM 16 kHz/16-bit/mono) vai direto para o
/// serviço, que devolve áudio traduzido a 24 kHz — convertido de volta para 16 kHz aqui
/// na borda. Diferente do Azure (ADR-0003), não há escolha de voz neste modo.
/// </para>
/// </summary>
public sealed class GeminiSpeechTranslator : ISpeechTranslator
{
    /// <summary>Único modelo documentado para o modo de tradução (preview em 2026-10-18).</summary>
    public const string DefaultModel = "gemini-3.5-live-translate-preview";

    /// <summary>
    /// A documentação do modo tradução pede blocos de 100 ms; o pipeline produz frames
    /// de 20 ms, então agrupamos cinco antes de enviar.
    /// </summary>
    private const int FramesPerChunk = 5;

    /// <summary>~4 s de áudio. Em backpressure descartamos o mais antigo, nunca acumulamos latência.</summary>
    private const int SendQueueCapacity = 200;

    private readonly Client _client;
    private readonly string _model;
    private readonly Channel<byte[]> _outgoing;
    private readonly Pcm24To16Resampler _resampler = new();

    private AsyncSession? _session;
    private CancellationTokenSource? _cts;
    private Task? _sendLoop;
    private Task? _receiveLoop;
    private string _lastSourceText = "";

    public event EventHandler<TranscriptEvent>? Transcript;
    public event EventHandler<ReadOnlyMemory<byte>>? SynthesizedAudio;
    public event EventHandler<Exception>? Faulted;

    public GeminiSpeechTranslator(Client client, string? model = null)
    {
        _client = client;
        _model = model ?? DefaultModel;
        _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(SendQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
    }

    /// <summary>
    /// Cria o tradutor a partir do ambiente: `GEMINI_API_KEY` (Google AI) ou, para Vertex AI,
    /// `GOOGLE_CLOUD_PROJECT` + `GOOGLE_CLOUD_LOCATION` com credencial padrão do gcloud.
    /// `GEMINI_LIVE_MODEL` permite fixar outro modelo.
    /// </summary>
    public static GeminiSpeechTranslator FromEnvironment() =>
        FromCredentials(new EnvironmentCredentialStore());

    /// <summary>
    /// Builds from any credential source (ADR-0009): environment variable, OS vault,
    /// or a chain of both. The variable names are unchanged.
    /// </summary>
    public static GeminiSpeechTranslator FromCredentials(ICredentialStore credentials)
    {
        var model = credentials.Read("GEMINI_LIVE_MODEL");

        if (credentials.Read("GEMINI_API_KEY") is { } apiKey)
            return new GeminiSpeechTranslator(new Client(apiKey: apiKey), model);

        var project = credentials.Read("GOOGLE_CLOUD_PROJECT");
        var location = credentials.Read("GOOGLE_CLOUD_LOCATION");
        if (project is not null && location is not null)
            return new GeminiSpeechTranslator(new Client(vertexAI: true, project: project, location: location), model);

        throw new InvalidOperationException(
            "Defina GEMINI_API_KEY, ou GOOGLE_CLOUD_PROJECT e GOOGLE_CLOUD_LOCATION para usar o Vertex AI.");
    }

    public async Task StartAsync(TranslatorOptions options, CancellationToken ct = default)
    {
        if (_session is not null)
            throw new InvalidOperationException("Translator is already started; call StopAsync first.");

        var config = new LiveConnectConfig
        {
            ResponseModalities = [Modality.Audio],
            TranslationConfig = new TranslationConfig
            {
                TargetLanguageCode = GeminiLanguages.ToTargetCode(options.TargetLanguage),
                // Áudio que já está no idioma alvo não deve voltar ecoado.
                EchoTargetLanguage = false
            },
            // Transcrições alimentam o RF-06 e a métrica de latência.
            InputAudioTranscription = new AudioTranscriptionConfig(),
            OutputAudioTranscription = new AudioTranscriptionConfig()
        };

        _session = await _client.Live.ConnectAsync(_model, config, ct);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _sendLoop = SendLoopAsync(_cts.Token);
        _receiveLoop = ReceiveLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Enfileira áudio canônico. Chamado da thread do pump: só copia e enfileira,
    /// nunca espera a rede.
    /// </summary>
    public void PushAudio(ReadOnlySpan<byte> pcm)
    {
        if (pcm.IsEmpty)
            return;
        _outgoing.Writer.TryWrite(pcm.ToArray());
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        var chunk = new List<byte[]>(FramesPerChunk);
        try
        {
            await foreach (var frame in _outgoing.Reader.ReadAllAsync(ct))
            {
                chunk.Add(frame);
                if (chunk.Count < FramesPerChunk)
                    continue;

                await SendChunkAsync(chunk, ct);
                chunk.Clear();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    private async Task SendChunkAsync(List<byte[]> frames, CancellationToken ct)
    {
        var total = frames.Sum(f => f.Length);
        var buffer = new byte[total];
        var offset = 0;
        foreach (var frame in frames)
        {
            frame.CopyTo(buffer, offset);
            offset += frame.Length;
        }

        await _session!.SendRealtimeInputAsync(
            new LiveSendRealtimeInputParameters
            {
                Audio = new Blob
                {
                    Data = buffer,
                    MimeType = $"audio/pcm;rate={AudioFrame.SampleRate}"
                }
            }, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await _session!.ReceiveAsync(ct);
                if (message is null)
                    break; // sessão encerrada pelo servidor

                Handle(message);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Fault(ex);
        }
    }

    private void Handle(LiveServerMessage message)
    {
        if (message.GoAway is { } goAway)
        {
            // O servidor vai encerrar a sessão. Sem RF-07 ainda, isso é uma falha.
            Fault(new InvalidOperationException(
                $"Gemini encerrará a sessão em {goAway.TimeLeft ?? "instantes"}; reconexão (RF-07) ainda não implementada."));
            return;
        }

        if (message.ServerContent is not { } content)
            return;

        if (content.InputTranscription?.Text is { Length: > 0 } source)
        {
            _lastSourceText = source;
            Transcript?.Invoke(this, new TranscriptEvent(source, "", IsFinal: false));
        }

        if (content.OutputTranscription?.Text is { Length: > 0 } translated)
            Transcript?.Invoke(this, new TranscriptEvent(
                _lastSourceText, translated, content.OutputTranscription.Finished ?? false));

        foreach (var part in content.ModelTurn?.Parts ?? [])
        {
            if (part.InlineData?.Data is not { Length: > 0 } audio24)
                continue;

            var audio16 = _resampler.Convert(audio24);
            if (audio16.Length > 0)
                SynthesizedAudio?.Invoke(this, audio16);
        }
    }

    private void Fault(Exception ex) => Faulted?.Invoke(this, ex);

    /// <summary>Encerra a sessão. Idempotente; um <see cref="StartAsync"/> posterior abre outra.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null)
            return;

        if (_cts is not null)
            await _cts.CancelAsync();

        foreach (var loop in new[] { _sendLoop, _receiveLoop })
        {
            if (loop is null) continue;
            try { await loop; } catch (OperationCanceledException) { }
        }
        _sendLoop = null;
        _receiveLoop = null;

        try
        {
            await session.CloseAsync();
        }
        finally
        {
            await session.DisposeAsync();
            _cts?.Dispose();
            _cts = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _client.Dispose();
    }
}
