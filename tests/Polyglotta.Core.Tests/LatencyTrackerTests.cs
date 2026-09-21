using Polyglotta.Core.Audio;
using Polyglotta.Core.Pipeline;
using Xunit;

namespace Polyglotta.Core.Tests;

public class LatencyTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static AudioFrame Voiced(DateTimeOffset at)
    {
        var pcm = new byte[AudioFrame.BytesPer20Ms];
        for (var i = 0; i < pcm.Length; i += 2)
            BitConverter.TryWriteBytes(pcm.AsSpan(i), (short)8000);
        return new AudioFrame(pcm, at);
    }

    private static AudioFrame Silent(DateTimeOffset at) => new(new byte[AudioFrame.BytesPer20Ms], at);

    [Fact]
    public void Sample_SpansLastVoicedFrame_ToFirstSynthesis_IgnoringTrailingSilence()
    {
        var tracker = new LatencyTracker();
        tracker.OnFrame(Voiced(T0));
        tracker.OnFrame(Voiced(T0.AddMilliseconds(500)));
        tracker.OnFrame(Silent(T0.AddMilliseconds(1000)));   // provider's end-of-speech window
        tracker.OnFinalTranscript();

        var latency = tracker.OnSynthesizedAudio(T0.AddMilliseconds(2300));

        Assert.Equal(TimeSpan.FromMilliseconds(1800), latency);
        Assert.Equal(1, tracker.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1800), tracker.Last);
    }

    [Fact]
    public void ContinuationChunks_DoNotProduceSamples()
    {
        var tracker = new LatencyTracker();
        tracker.OnFrame(Voiced(T0));
        tracker.OnFinalTranscript();

        Assert.NotNull(tracker.OnSynthesizedAudio(T0.AddSeconds(1)));
        Assert.Null(tracker.OnSynthesizedAudio(T0.AddSeconds(1.1)));
        Assert.Null(tracker.OnSynthesizedAudio(T0.AddSeconds(1.2)));
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void Synthesis_WithoutFinalTranscript_IsIgnored()
    {
        var tracker = new LatencyTracker();
        tracker.OnFrame(Voiced(T0));
        Assert.Null(tracker.OnSynthesizedAudio(T0.AddSeconds(1)));
        Assert.Null(tracker.Last);
    }

    [Fact]
    public void FinalTranscript_WithoutAnyVoice_DoesNotArm()
    {
        var tracker = new LatencyTracker();
        tracker.OnFrame(Silent(T0));
        tracker.OnFinalTranscript();
        Assert.Null(tracker.OnSynthesizedAudio(T0.AddSeconds(1)));
    }

    [Fact]
    public void P95_IsNearestRank()
    {
        var tracker = new LatencyTracker();
        for (var i = 1; i <= 20; i++)
        {
            tracker.OnFrame(Voiced(T0));
            tracker.OnFinalTranscript();
            tracker.OnSynthesizedAudio(T0.AddMilliseconds(i * 100)); // 100, 200, ..., 2000 ms
        }
        Assert.Equal(20, tracker.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1900), tracker.P95); // rank ceil(0.95*20)=19 -> 1900
    }
}
