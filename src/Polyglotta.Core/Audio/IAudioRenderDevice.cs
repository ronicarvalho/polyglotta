namespace Polyglotta.Core.Audio;

/// <summary>
/// Plays canonical-format PCM on an output device. Used both for the virtual
/// microphone (outbound translated speech) and the user's headphones (inbound).
/// </summary>
public interface IAudioRenderDevice : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct = default);
    /// <summary>Enqueue PCM (16 kHz/16-bit/mono) for playback. Non-blocking.</summary>
    void Enqueue(ReadOnlyMemory<byte> pcm);
    Task StopAsync(CancellationToken ct = default);
}
