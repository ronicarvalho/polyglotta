namespace Polyglotta.Core.Translation;

public sealed record TranslatorOptions(
    string SourceLanguage,   // e.g. "pt-BR"
    string TargetLanguage,   // e.g. "en-US" (BCP-47); provider maps to its own codes
    string? VoiceName);      // e.g. "en-US-JennyNeural"

public sealed record TranscriptEvent(string SourceText, string TranslatedText, bool IsFinal);

/// <summary>
/// Streaming speech-to-speech translation (ADR-0003). Push canonical PCM in,
/// receive synthesized audio and transcript events out.
/// </summary>
public interface ISpeechTranslator : IAsyncDisposable
{
    event EventHandler<TranscriptEvent>? Transcript;
    /// <summary>Synthesized translated speech, PCM 16 kHz/16-bit/mono.</summary>
    event EventHandler<ReadOnlyMemory<byte>>? SynthesizedAudio;
    event EventHandler<Exception>? Faulted;

    Task StartAsync(TranslatorOptions options, CancellationToken ct = default);
    void PushAudio(ReadOnlySpan<byte> pcm);
    Task StopAsync(CancellationToken ct = default);
}
