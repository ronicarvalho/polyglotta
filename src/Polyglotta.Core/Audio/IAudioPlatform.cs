namespace Polyglotta.Core.Audio;

/// <summary>
/// The platform's audio capabilities, as the orchestration needs them (ADR-0004).
/// <para>
/// Lets everything above the device layer — session orchestration, CLI, tray — stay
/// free of WASAPI, and lets the whole thing be driven by fakes in tests. A macOS or
/// Linux port is one more implementation of this interface.
/// </para>
/// </summary>
public interface IAudioPlatform
{
    IAudioDeviceEnumerator Devices { get; }

    /// <summary>Captures from an input endpoint (the physical microphone).</summary>
    IAudioCaptureDevice CreateMicrophoneCapture(string deviceId);

    /// <summary>
    /// Taps what is being played on an <em>output</em> endpoint — the remote party's
    /// voice. The id is of a render device, not a capture one.
    /// </summary>
    ISystemAudioCapture CreateLoopbackCapture(string renderDeviceId);

    /// <summary>Plays canonical PCM on an output endpoint (virtual mic or headphones).</summary>
    IAudioRenderDevice CreateRenderDevice(string deviceId);
}
