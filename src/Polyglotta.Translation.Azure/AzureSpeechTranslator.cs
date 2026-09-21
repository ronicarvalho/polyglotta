using Polyglotta.Core.Audio;
using Polyglotta.Core.Translation;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;

namespace Polyglotta.Translation.Azure;

/// <summary>
/// ISpeechTranslator backed by Azure Speech Translation with integrated voice
/// synthesis (ADR-0003). Input/output: PCM 16 kHz/16-bit/mono.
/// </summary>
public sealed class AzureSpeechTranslator : ISpeechTranslator
{
    private readonly string _key;
    private readonly string _region;
    private PushAudioInputStream? _push;
    private TranslationRecognizer? _recognizer;
    private string _targetLangShort = "en";

    public event EventHandler<TranscriptEvent>? Transcript;
    public event EventHandler<ReadOnlyMemory<byte>>? SynthesizedAudio;
    public event EventHandler<Exception>? Faulted;

    public AzureSpeechTranslator(string key, string region)
    {
        _key = key;
        _region = region;
    }

    public static AzureSpeechTranslator FromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY")
                  ?? throw new InvalidOperationException("AZURE_SPEECH_KEY not set");
        var region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION")
                  ?? throw new InvalidOperationException("AZURE_SPEECH_REGION not set");
        return new AzureSpeechTranslator(key, region);
    }

    public async Task StartAsync(TranslatorOptions options, CancellationToken ct = default)
    {
        if (_recognizer is not null)
            throw new InvalidOperationException("Translator is already started; call StopAsync first.");

        var config = SpeechTranslationConfig.FromSubscription(_key, _region);
        config.SpeechRecognitionLanguage = options.SourceLanguage;

        // Azure expects short codes for translation targets ("en", "pt"), full
        // locale for the synthesis voice.
        _targetLangShort = options.TargetLanguage.Split('-')[0];
        config.AddTargetLanguage(_targetLangShort);
        if (options.VoiceName is not null)
            config.VoiceName = options.VoiceName;
        config.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Raw16Khz16BitMonoPcm);

        _push = AudioInputStream.CreatePushStream(
            AudioStreamFormat.GetWaveFormatPCM(AudioFrame.SampleRate, AudioFrame.BitsPerSample, AudioFrame.Channels));
        _recognizer = new TranslationRecognizer(config, AudioConfig.FromStreamInput(_push));

        _recognizer.Recognizing += (_, e) =>
        {
            if (e.Result.Translations.TryGetValue(_targetLangShort, out var partial))
                Transcript?.Invoke(this, new TranscriptEvent(e.Result.Text, partial, IsFinal: false));
        };

        _recognizer.Recognized += (_, e) =>
        {
            if (e.Result.Reason == ResultReason.TranslatedSpeech &&
                e.Result.Translations.TryGetValue(_targetLangShort, out var final))
                Transcript?.Invoke(this, new TranscriptEvent(e.Result.Text, final, IsFinal: true));
        };

        _recognizer.Synthesizing += (_, e) =>
        {
            var audio = e.Result.GetAudio();
            if (audio.Length > 0)
                SynthesizedAudio?.Invoke(this, audio);
        };

        _recognizer.Canceled += (_, e) =>
        {
            if (e.Reason == CancellationReason.Error)
                Faulted?.Invoke(this, new InvalidOperationException(
                    $"Azure Speech canceled: {e.ErrorCode} — {e.ErrorDetails}"));
        };

        await _recognizer.StartContinuousRecognitionAsync().WaitAsync(ct);
    }

    public void PushAudio(ReadOnlySpan<byte> pcm) => _push?.Write(pcm.ToArray());

    /// <summary>Stops and releases the session. Idempotent; a later <see cref="StartAsync"/> opens a new one.</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        var recognizer = Interlocked.Exchange(ref _recognizer, null);
        var push = Interlocked.Exchange(ref _push, null);
        if (recognizer is null)
            return;

        try
        {
            await recognizer.StopContinuousRecognitionAsync().WaitAsync(ct);
        }
        finally
        {
            push?.Close();
            recognizer.Dispose();
            push?.Dispose();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
