namespace Polyglotta.Core.Audio;

public interface IAudioDeviceEnumerator
{
    IReadOnlyList<AudioDeviceInfo> ListDevices();

    /// <summary>Finds a device whose name contains <paramref name="nameFragment"/> (case-insensitive), or null.</summary>
    AudioDeviceInfo? FindByName(string nameFragment, AudioDeviceKind kind);
}
