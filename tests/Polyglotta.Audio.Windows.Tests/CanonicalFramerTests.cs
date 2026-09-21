using System.Threading.Channels;
using Polyglotta.Audio.Windows;
using Polyglotta.Core.Audio;
using NAudio.Wave;
using Xunit;

namespace Polyglotta.Audio.Windows.Tests;

/// <summary>
/// Pure-logic tests for the edge converter/framer. No audio hardware involved;
/// device I/O is covered by the manual test script in the PR.
/// </summary>
public class CanonicalFramerTests
{
    private static readonly WaveFormat Canonical = new(AudioFrame.SampleRate, AudioFrame.BitsPerSample, AudioFrame.Channels);

    private static List<AudioFrame> Drain(Channel<AudioFrame> ch)
    {
        var list = new List<AudioFrame>();
        while (ch.Reader.TryRead(out var f)) list.Add(f);
        return list;
    }

    [Fact]
    public void CanonicalInput_IsPassThrough_AndSlicedInto20MsFrames()
    {
        var framer = new CanonicalFramer(Canonical);
        Assert.True(framer.IsPassThrough);

        var ch = Channel.CreateUnbounded<AudioFrame>();
        // 100 ms delivered as one driver buffer → five 640-byte frames
        framer.Feed(new byte[AudioFrame.BytesPer20Ms * 5], ch.Writer, DateTimeOffset.UtcNow);

        var frames = Drain(ch);
        Assert.Equal(5, frames.Count);
        Assert.All(frames, f => Assert.Equal(AudioFrame.BytesPer20Ms, f.Pcm.Length));
    }

    [Fact]
    public void PartialBuffers_AreAccumulatedAcrossCalls_WithoutLosingBytes()
    {
        var framer = new CanonicalFramer(Canonical);
        var ch = Channel.CreateUnbounded<AudioFrame>();

        // Sequential sample values so we can verify ordering across the frame boundary.
        var input = Enumerable.Range(0, AudioFrame.BytesPer20Ms * 3).Select(i => (byte)i).ToArray();
        framer.Feed(input.AsSpan(0, 1000), ch.Writer, DateTimeOffset.UtcNow);   // 1 frame + 360 B pending
        Assert.Single(Drain(ch));

        framer.Feed(input.AsSpan(1000), ch.Writer, DateTimeOffset.UtcNow);      // completes 2 more
        var rest = Drain(ch);
        Assert.Equal(2, rest.Count);
        Assert.Equal(input.AsSpan(AudioFrame.BytesPer20Ms, AudioFrame.BytesPer20Ms).ToArray(), rest[0].Pcm);
        Assert.Equal(input.AsSpan(AudioFrame.BytesPer20Ms * 2).ToArray(), rest[1].Pcm);
    }

    [Fact]
    public void Feed_WithNoData_EmitsNothing()
    {
        var framer = new CanonicalFramer(Canonical);
        var ch = Channel.CreateUnbounded<AudioFrame>();
        framer.Feed(ReadOnlySpan<byte>.Empty, ch.Writer, DateTimeOffset.UtcNow);
        Assert.Empty(Drain(ch));
    }

    [Fact]
    public void StereoFloat48k_IsConvertedToCanonical_AndTerminates()
    {
        // Typical WASAPI mix format. Regression for the ReadFully infinite loop (review B3).
        var native = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        var framer = new CanonicalFramer(native);
        Assert.False(framer.IsPassThrough);

        var ch = Channel.CreateUnbounded<AudioFrame>();
        // 200 ms of native audio = 0.2 s * 48000 * 2 ch * 4 B
        var native200Ms = new byte[48_000 / 5 * 2 * 4];
        var task = Task.Run(() => framer.Feed(native200Ms, ch.Writer, DateTimeOffset.UtcNow));
        Assert.True(task.Wait(TimeSpan.FromSeconds(5)), "Feed must terminate when the input is drained");

        // ~200 ms canonical = 10 frames; the resampler may hold back a few samples of lookahead.
        var frames = Drain(ch);
        Assert.InRange(frames.Count, 8, 10);
        Assert.All(frames, f => Assert.Equal(AudioFrame.BytesPer20Ms, f.Pcm.Length));
    }

    [Fact]
    public void ConvertedAudio_PreservesSignal()
    {
        // 1 kHz sine at 48 kHz stereo must come out as a non-silent 16-bit signal.
        var native = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        var framer = new CanonicalFramer(native);
        var ch = Channel.CreateUnbounded<AudioFrame>();

        const int samples = 48_000 / 5; // 200 ms
        var buf = new byte[samples * 2 * 4];
        for (var i = 0; i < samples; i++)
        {
            var v = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 48_000.0));
            BitConverter.TryWriteBytes(buf.AsSpan(i * 8), v);      // L
            BitConverter.TryWriteBytes(buf.AsSpan(i * 8 + 4), v);  // R
        }
        framer.Feed(buf, ch.Writer, DateTimeOffset.UtcNow);

        var frames = Drain(ch);
        Assert.NotEmpty(frames);
        var peak = frames.Skip(1).SelectMany(f => Enumerable.Range(0, f.Pcm.Length / 2)
            .Select(i => Math.Abs((int)BitConverter.ToInt16(f.Pcm, i * 2)))).Max();
        Assert.InRange(peak, 12_000, 18_000); // 0.5 full-scale ≈ 16384
    }

    [Fact]
    public void MoreThanTwoChannels_IsRejected()
    {
        var native = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 6);
        Assert.Throws<NotSupportedException>(() => new CanonicalFramer(native));
    }
}
