using System.Threading.Channels;
using Polyglotta.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Polyglotta.Audio.Windows;

/// <summary>
/// Shared plumbing for WASAPI captures built on <see cref="WasapiRecorder"/> (ADR-0005):
/// owns the bounded frame channel and the <see cref="CanonicalFramer"/>. Subclasses only
/// decide how the recorder is built (mic vs. loopback).
/// </summary>
public abstract class WasapiRecorderCapture : IAudioCaptureDevice
{
    /// <summary>~4 s of 20 ms frames. Under backpressure old audio is dropped, never buffered.</summary>
    private const int ChannelCapacity = 200;

    private readonly Channel<AudioFrame> _channel = Channel.CreateBounded<AudioFrame>(
        new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

    private readonly WasapiRecorder _recorder;
    private readonly CanonicalFramer _framer;

    public ChannelReader<AudioFrame> Frames => _channel.Reader;

    /// <summary>Endpoint ID actually opened (same string the enumerator reports).</summary>
    public string DeviceId => _recorder.DeviceId;
    public string DeviceName => _recorder.DeviceFriendlyName;

    /// <summary>True when WASAPI delivers the canonical format and no resampling runs in-process.</summary>
    public bool IsNativeCanonical => _framer.IsPassThrough;

    private protected WasapiRecorderCapture(WasapiRecorder recorder)
    {
        _recorder = recorder;
        _framer = new CanonicalFramer(recorder.WaveFormat);

        // Audio thread: convert + enqueue only. TryWrite never blocks (DropOldest).
        _recorder.DataAvailable += OnDataAvailable;
        _recorder.RecordingStopped += (_, e) => _channel.Writer.TryComplete(e.Exception);
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        => _framer.Feed(buffer, _channel.Writer, DateTimeOffset.UtcNow);

    public Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _recorder.StartRecording();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (_recorder.CaptureState != CaptureState.Stopped)
            _recorder.StopRecording();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _recorder.DataAvailable -= OnDataAvailable;
        await _recorder.DisposeAsync();
        _channel.Writer.TryComplete();
    }

    /// <summary>Canonical format requested from the engine (AutoConvertPcm in shared mode).</summary>
    private protected static WaveFormat CanonicalWaveFormat { get; } =
        new(AudioFrame.SampleRate, AudioFrame.BitsPerSample, AudioFrame.Channels);

    /// <summary>Driver callback period. 20 ms matches the canonical frame (audio-realtime skill).</summary>
    private protected const int BufferLengthMs = 20;
}
