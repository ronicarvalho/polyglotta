using Polyglotta.Core.Audio;
using Xunit;

namespace Polyglotta.Core.Tests;

public class AudioFrameTests
{
    [Fact]
    public void BytesPer20Ms_MatchesCanonicalFormat()
    {
        // 16 kHz * 16-bit (2 bytes) * mono * 0.02 s = 640 bytes
        Assert.Equal(640, AudioFrame.BytesPer20Ms);
    }

    [Fact]
    public void Duration_IsComputedFromPcmLength()
    {
        var frame = new AudioFrame(new byte[AudioFrame.BytesPer20Ms], DateTimeOffset.UtcNow);
        Assert.Equal(20, frame.Duration.TotalMilliseconds, precision: 3);
    }
}
