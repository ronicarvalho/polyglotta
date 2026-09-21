using System.Threading.Channels;

namespace Polyglotta.Core.Audio;

/// <summary>
/// Captures audio from a device (e.g. physical microphone) and writes canonical
/// <see cref="AudioFrame"/>s to <see cref="Frames"/>. Implementations must convert
/// to 16 kHz/16-bit/mono at the edge and never block inside driver callbacks.
/// </summary>
public interface IAudioCaptureDevice : IAsyncDisposable
{
    ChannelReader<AudioFrame> Frames { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
