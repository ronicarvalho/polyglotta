namespace Polyglotta.Core.Audio;

/// <summary>
/// Canonical pipeline format: PCM 16-bit, 16 kHz, mono (see ADR-0003).
/// Platform layers must convert to this format at the edge.
/// </summary>
public sealed record AudioFrame(byte[] Pcm, DateTimeOffset CapturedAt)
{
    public const int SampleRate = 16_000;
    public const int BitsPerSample = 16;
    public const int Channels = 1;

    /// <summary>Bytes in a 20 ms frame at the canonical format.</summary>
    public const int BytesPer20Ms = SampleRate / 50 * (BitsPerSample / 8) * Channels; // 640

    public TimeSpan Duration =>
        TimeSpan.FromSeconds((double)Pcm.Length / (SampleRate * (BitsPerSample / 8) * Channels));
}
