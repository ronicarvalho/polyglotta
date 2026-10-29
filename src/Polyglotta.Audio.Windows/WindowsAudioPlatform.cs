using Polyglotta.Core.Audio;

namespace Polyglotta.Audio.Windows;

/// <summary>
/// WASAPI implementation of <see cref="IAudioPlatform"/> (ADR-0004). The only place
/// above the device classes that knows the session runs on Windows.
/// </summary>
public sealed class WindowsAudioPlatform : IAudioPlatform
{
    public IAudioDeviceEnumerator Devices { get; } = new WindowsAudioDeviceEnumerator();

    public IAudioCaptureDevice CreateMicrophoneCapture(string deviceId) => new WasapiMicCapture(deviceId);

    public ISystemAudioCapture CreateLoopbackCapture(string renderDeviceId) =>
        new WasapiLoopbackSystemCapture(renderDeviceId);

    public IAudioRenderDevice CreateRenderDevice(string deviceId) => new WasapiRenderDevice(deviceId);
}
