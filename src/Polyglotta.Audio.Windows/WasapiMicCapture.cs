using System.Threading.Channels;
using Polyglotta.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Polyglotta.Audio.Windows;

/// <summary>Captures the physical microphone via WASAPI and emits canonical frames.</summary>
public sealed class WasapiMicCapture : IAudioCaptureDevice
{
    private readonly Channel<AudioFrame> _channel;
    private readonly WasapiCapture _capture;
    private readonly CanonicalFormatConverter _converter;

    public ChannelReader<AudioFrame> Frames => _channel.Reader;

    public WasapiMicCapture(string? deviceId = null)
    {
        _channel = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(capacity: 200)
        {
            FullMode = BoundedChannelFullMode.DropOldest // never grow latency under backpressure
        });

        _capture = deviceId is null
            ? new WasapiCapture()
            : new WasapiCapture(WindowsAudioDeviceEnumerator.GetById(deviceId));

        _converter = new CanonicalFormatConverter(_capture.WaveFormat);

        // Audio thread: copy + enqueue only. No I/O, no locks (audio-realtime skill).
        _capture.DataAvailable += (_, e) =>
        {
            var pcm = _converter.Convert(e.Buffer, e.BytesRecorded);
            if (pcm.Length > 0)
                _channel.Writer.TryWrite(new AudioFrame(pcm, DateTimeOffset.UtcNow));
        };
        _capture.RecordingStopped += (_, _) => _channel.Writer.TryComplete();
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _capture.StartRecording();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _capture.StopRecording();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _capture.Dispose();
        return ValueTask.CompletedTask;
    }
}
