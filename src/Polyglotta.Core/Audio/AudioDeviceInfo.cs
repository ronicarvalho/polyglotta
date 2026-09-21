namespace Polyglotta.Core.Audio;

public enum AudioDeviceKind { Capture, Render }

public sealed record AudioDeviceInfo(string Id, string Name, AudioDeviceKind Kind, bool IsDefault);
