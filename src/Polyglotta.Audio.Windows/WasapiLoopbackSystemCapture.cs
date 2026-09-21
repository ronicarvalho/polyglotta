using System.Threading.Channels;
using Polyglotta.Core.Audio;
using NAudio.Wave;

namespace Polyglotta.Audio.Windows;

/// <summary>
/// Captures whatever the system plays on the default render device (the remote
/// party's voice in Meet/Teams) via WASAPI loopback.
/// WARNING: this also captures audio this app plays on the same device — route
/// inbound translated playback to a different device (audio-realtime skill).
/// </summary>
public sealed class WasapiLoopbackSystemCapture : ISystemAudioCapture
{
    private readonly Channel<AudioFrame> _channel;
    private readonly WasapiLoopbackCapture _capture;
    private readonly CanonicalFormatConverter _converter;

    public ChannelReader<AudioFrame> Frames => _channel.Reader;

    public WasapiLoopbackSystemCapture(string? renderDeviceId = null)
    {
        _channel = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(200)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _capture = renderDeviceId is null
            ? new WasapiLoopbackCapture()
            : new WasapiLoopbackCapture(WindowsAudioDeviceEnumerator.GetById(renderDeviceId));

        _converter = new CanonicalFormatConverter(_capture.WaveFormat);

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
