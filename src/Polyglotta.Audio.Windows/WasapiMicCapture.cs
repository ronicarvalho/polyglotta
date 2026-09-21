using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Polyglotta.Audio.Windows;

/// <summary>Captures the physical microphone via WASAPI and emits canonical 20 ms frames.</summary>
public sealed class WasapiMicCapture : WasapiRecorderCapture
{
    /// <param name="deviceId">Capture endpoint ID, or null for the default communications device.</param>
    public WasapiMicCapture(string? deviceId = null)
        : base(() => Build(deviceId))
    {
    }

    private static WasapiRecorder Build(string? deviceId)
    {
        var device = deviceId is null
            ? WindowsAudioDeviceEnumerator.GetDefault(DataFlow.Capture)
            : WindowsAudioDeviceEnumerator.GetById(deviceId);

        // Communications mode enables the OS voice pipeline (AEC/noise suppression)
        // and matches the "default" the enumerator reports (Role.Communications).
        return new WasapiRecorderBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithCommunicationsMode()
            .WithFormat(CanonicalWaveFormat)
            .WithBufferLength(BufferLengthMs)
            .Build();
    }
}
