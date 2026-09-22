using Polyglotta.Core.Audio;
using Xunit;

namespace Polyglotta.Core.Tests;

public class AudioLevelMeterTests
{
    private static AudioFrame Frame(short amplitude)
    {
        var pcm = new byte[AudioFrame.BytesPer20Ms];
        for (var i = 0; i < pcm.Length; i += 2)
            BitConverter.TryWriteBytes(pcm.AsSpan(i), amplitude);
        return new AudioFrame(pcm, DateTimeOffset.UtcNow);
    }

    /// <summary>Frames worth exactly one second of audio.</summary>
    private static void AddSeconds(AudioLevelMeter meter, double seconds, short amplitude)
    {
        for (var i = 0; i < (int)(seconds * 50); i++)
            meter.Add(Frame(amplitude));
    }

    [Fact]
    public void NoReport_BeforeTheWindowElapses()
    {
        var meter = new AudioLevelMeter(TimeSpan.FromSeconds(5));
        AddSeconds(meter, 4.9, 1000);
        Assert.Null(meter.TryReport());
    }

    [Fact]
    public void Report_CountsFramesVoiceAndPeak()
    {
        var meter = new AudioLevelMeter(TimeSpan.FromSeconds(1));
        AddSeconds(meter, 0.5, 100);    // below the 500 threshold
        AddSeconds(meter, 0.5, 8000);   // voiced

        var report = meter.TryReport();

        Assert.NotNull(report);
        Assert.Equal(50, report!.Value.Frames);
        Assert.Equal(25, report.Value.VoicedFrames);
        Assert.Equal(8000, report.Value.PeakRms, precision: 0);
        Assert.Equal(TimeSpan.FromSeconds(1), report.Value.Window);
        Assert.False(report.Value.LooksSilent);
    }

    [Fact]
    public void WindowResets_AfterEachReport()
    {
        var meter = new AudioLevelMeter(TimeSpan.FromSeconds(1));
        AddSeconds(meter, 1, 9000);
        Assert.NotNull(meter.TryReport());
        Assert.Null(meter.TryReport());

        AddSeconds(meter, 1, 200);
        var second = meter.TryReport();
        Assert.NotNull(second);
        Assert.Equal(200, second!.Value.PeakRms, precision: 0);  // peak is not carried over
        Assert.Equal(0, second.Value.VoicedFrames);
    }

    [Fact]
    public void LooksSilent_WhenOnlyNoiseFloorArrives()
    {
        // A disconnected analog input: frames keep coming, peak stays near zero.
        var meter = new AudioLevelMeter(TimeSpan.FromSeconds(1));
        AddSeconds(meter, 1, 11);

        var report = meter.TryReport();

        Assert.True(report!.Value.LooksSilent);
        Assert.Equal(0, report.Value.VoicedFrames);
        Assert.InRange(report.Value.PeakScale, 0, 0.01);
    }

    [Fact]
    public void EmptyMeter_ReportsNothing() =>
        Assert.Null(new AudioLevelMeter(TimeSpan.FromSeconds(1)).TryReport());
}
