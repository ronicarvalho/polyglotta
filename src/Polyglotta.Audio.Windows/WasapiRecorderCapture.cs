using System.Threading.Channels;
using Polyglotta.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Polyglotta.Audio.Windows;

/// <summary>
/// Shared plumbing for WASAPI captures built on <see cref="WasapiRecorder"/> (ADR-0005):
/// owns the bounded frame channel and the <see cref="CanonicalFramer"/>. Subclasses only
/// decide how the recorder is built (mic vs. loopback).
/// <para>
/// A <see cref="WasapiRecorder"/> cannot be restarted once stopped (NAudio 3.1.0 throws
/// "audio client is already initialized"), so every <see cref="StartAsync"/> builds a fresh
/// one from the factory. <see cref="Frames"/> stays open across restarts and only completes
/// on device error or dispose.
/// </para>
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

    private readonly Func<WasapiRecorder> _factory;
    private WasapiRecorder _recorder;
    private CanonicalFramer _framer;
    private bool _recorderUsed;
    private bool _disposed;

    public ChannelReader<AudioFrame> Frames => _channel.Reader;

    /// <summary>Endpoint ID actually opened (same string the enumerator reports).</summary>
    public string DeviceId => _recorder.DeviceId;
    public string DeviceName => _recorder.DeviceFriendlyName;

    /// <summary>True when WASAPI delivers the canonical format and no resampling runs in-process.</summary>
    public bool IsNativeCanonical => _framer.IsPassThrough;

    private protected WasapiRecorderCapture(Func<WasapiRecorder> factory)
    {
        _factory = factory;
        (_recorder, _framer) = Attach(factory());
    }

    private (WasapiRecorder, CanonicalFramer) Attach(WasapiRecorder recorder)
    {
        var framer = new CanonicalFramer(recorder.WaveFormat);
        // Audio thread: convert + enqueue only. TryWrite never blocks (DropOldest).
        recorder.DataAvailable += OnDataAvailable;
        recorder.RecordingStopped += OnRecordingStopped;
        return (recorder, framer);
    }

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        => _framer.Feed(buffer, _channel.Writer, DateTimeOffset.UtcNow);

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // A normal stop keeps the channel open for a later restart; a device error ends it
        // so the pipeline pump observes the failure.
        if (e.Exception is not null)
            _channel.Writer.TryComplete(e.Exception);
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_recorder.CaptureState != CaptureState.Stopped)
            return;

        if (_recorderUsed) // single-use: rebuild before the second start
        {
            await DetachAndDisposeAsync(_recorder);
            (_recorder, _framer) = Attach(_factory());
        }
        _recorder.StartRecording();
        _recorderUsed = true;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        if (_recorder.CaptureState != CaptureState.Stopped)
            _recorder.StopRecording();
        return Task.CompletedTask;
    }

    private async ValueTask DetachAndDisposeAsync(WasapiRecorder recorder)
    {
        recorder.DataAvailable -= OnDataAvailable;
        recorder.RecordingStopped -= OnRecordingStopped;
        await recorder.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await DetachAndDisposeAsync(_recorder);
        _channel.Writer.TryComplete();
    }

    /// <summary>Canonical format requested from the engine (AutoConvertPcm in shared mode).</summary>
    private protected static WaveFormat CanonicalWaveFormat { get; } =
        new(AudioFrame.SampleRate, AudioFrame.BitsPerSample, AudioFrame.Channels);

    /// <summary>Driver callback period. 20 ms matches the canonical frame (audio-realtime skill).</summary>
    private protected const int BufferLengthMs = 20;
}
