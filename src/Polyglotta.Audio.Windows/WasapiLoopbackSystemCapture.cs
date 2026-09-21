using Polyglotta.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Polyglotta.Audio.Windows;

/// <summary>
/// Captures whatever the system plays on a render device (the remote party's voice in
/// Meet/Teams) via WASAPI loopback.
/// <para>
/// WARNING: this also captures audio this app plays on the same device — route inbound
/// translated playback to a different device (audio-realtime skill). Compare
/// <see cref="WasapiRecorderCapture.DeviceId"/> with the render device before starting.
/// </para>
/// WASAPI loopback only delivers packets while some application is rendering on the
/// device; with nothing playing, <see cref="WasapiRecorderCapture.Frames"/> stays silent
/// (verified 2026-09-21, NAudio 3.1.0).
/// </summary>
public sealed class WasapiLoopbackSystemCapture : WasapiRecorderCapture, ISystemAudioCapture
{
    /// <param name="renderDeviceId">Render endpoint ID to tap, or null for the default communications device.</param>
    public WasapiLoopbackSystemCapture(string? renderDeviceId = null)
        : base(Build(renderDeviceId))
    {
    }

    private static WasapiRecorder Build(string? renderDeviceId)
    {
        var device = renderDeviceId is null
            ? WindowsAudioDeviceEnumerator.GetDefault(DataFlow.Render)
            : WindowsAudioDeviceEnumerator.GetById(renderDeviceId);

        return new WasapiRecorderBuilder()
            .WithDevice(device)
            .WithLoopbackCapture()
            .WithFormat(CanonicalWaveFormat)
            .WithBufferLength(BufferLengthMs)
            .Build();
    }
}
