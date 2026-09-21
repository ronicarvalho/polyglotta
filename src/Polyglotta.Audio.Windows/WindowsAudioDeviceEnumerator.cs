using Polyglotta.Core.Audio;
using NAudio.CoreAudioApi;

namespace Polyglotta.Audio.Windows;

public sealed class WindowsAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    public IReadOnlyList<AudioDeviceInfo> ListDevices()
    {
        using var mm = new MMDeviceEnumerator();
        var result = new List<AudioDeviceInfo>();

        var defaultCapture = TryGetDefault(mm, DataFlow.Capture);
        var defaultRender = TryGetDefault(mm, DataFlow.Render);

        foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            result.Add(new AudioDeviceInfo(d.ID, d.FriendlyName, AudioDeviceKind.Capture, d.ID == defaultCapture));
        foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            result.Add(new AudioDeviceInfo(d.ID, d.FriendlyName, AudioDeviceKind.Render, d.ID == defaultRender));

        return result;
    }

    public AudioDeviceInfo? FindByName(string nameFragment, AudioDeviceKind kind) =>
        ListDevices().FirstOrDefault(d =>
            d.Kind == kind &&
            d.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase));

    private static string? TryGetDefault(MMDeviceEnumerator mm, DataFlow flow)
    {
        try { return mm.GetDefaultAudioEndpoint(flow, Role.Communications).ID; }
        catch { return null; }
    }

    internal static MMDevice GetById(string id)
    {
        using var mm = new MMDeviceEnumerator();
        return mm.GetDevice(id);
    }

    /// <summary>Default communications endpoint — the same one <see cref="ListDevices"/> flags as default.</summary>
    internal static MMDevice GetDefault(DataFlow flow)
    {
        using var mm = new MMDeviceEnumerator();
        return mm.GetDefaultAudioEndpoint(flow, Role.Communications);
    }
}
