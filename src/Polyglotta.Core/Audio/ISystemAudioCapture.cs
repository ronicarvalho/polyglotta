namespace Polyglotta.Core.Audio;

/// <summary>
/// Captures what the system is playing (the remote party's voice in a call).
/// Windows: WASAPI loopback. Mac: BlackHole. Linux: PipeWire monitor source.
/// </summary>
public interface ISystemAudioCapture : IAudioCaptureDevice;
