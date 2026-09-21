using Polyglotta.Core.Audio;
using NAudio.Wave;

namespace Polyglotta.Audio.Windows;

/// <summary>
/// Plays canonical PCM on a WASAPI render device. Point it at "CABLE Input" for the
/// outbound virtual mic (ADR-0002) or at the user's headphones for inbound.
/// Shared mode: the audio engine converts 16 kHz/mono to the device mix format.
/// </summary>
public sealed class WasapiRenderDevice : IAudioRenderDevice
{
    /// <summary>Synthesized sentences can be several seconds; older audio is dropped past this.</summary>
    private static readonly TimeSpan QueueDuration = TimeSpan.FromSeconds(30);

    private readonly WasapiPlayer _player;
    private readonly BufferedWaveProvider _queue;

    public string DeviceId => _player.DeviceId;
    public string DeviceName => _player.DeviceFriendlyName;

    public WasapiRenderDevice(string deviceId)
    {
        _player = new WasapiPlayerBuilder()
            .WithDevice(WindowsAudioDeviceEnumerator.GetById(deviceId))
            .WithSharedMode()
            .WithEventSync()
            .WithLatency(50)
            .Build();

        _queue = new BufferedWaveProvider(
            new WaveFormat(AudioFrame.SampleRate, AudioFrame.BitsPerSample, AudioFrame.Channels), QueueDuration)
        {
            DiscardOnBufferOverflow = true
        };
        _player.Init(_queue);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _player.Play();
        return Task.CompletedTask;
    }

    public void Enqueue(ReadOnlyMemory<byte> pcm) => _queue.AddSamples(pcm.Span);

    public Task StopAsync(CancellationToken ct = default)
    {
        if (_player.PlaybackState != PlaybackState.Stopped)
            _player.Stop();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _player.DisposeAsync();
}
