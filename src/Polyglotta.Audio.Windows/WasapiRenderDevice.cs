using Polyglotta.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Polyglotta.Audio.Windows;

/// <summary>
/// Plays canonical PCM on a WASAPI render device. Point it at "CABLE Input"
/// for the outbound virtual mic (ADR-0002) or at the user's headphones for inbound.
/// </summary>
public sealed class WasapiRenderDevice : IAudioRenderDevice
{
    private readonly WasapiOut _out;
    private readonly BufferedWaveProvider _buffer;

    public WasapiRenderDevice(string deviceId)
    {
        var device = WindowsAudioDeviceEnumerator.GetById(deviceId);
        _out = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 50);

        _buffer = new BufferedWaveProvider(
            new WaveFormat(AudioFrame.SampleRate, AudioFrame.BitsPerSample, AudioFrame.Channels))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(30) // synthesized sentences can be long
        };
        _out.Init(_buffer);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _out.Play();
        return Task.CompletedTask;
    }

    public void Enqueue(ReadOnlyMemory<byte> pcm)
    {
        var arr = pcm.ToArray();
        _buffer.AddSamples(arr, 0, arr.Length);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _out.Stop();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _out.Dispose();
        return ValueTask.CompletedTask;
    }
}
