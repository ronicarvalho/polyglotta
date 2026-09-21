using Polyglotta.Core.Audio;
using Xunit;

namespace Polyglotta.Core.Tests;

public class PcmMathTests
{
    [Fact]
    public void Rms_OfSilence_IsZero() => Assert.Equal(0, PcmMath.Rms(new byte[640]));

    [Fact]
    public void Rms_OfEmpty_IsZero() => Assert.Equal(0, PcmMath.Rms(ReadOnlySpan<byte>.Empty));

    [Fact]
    public void Rms_OfConstantSignal_IsItsAmplitude()
    {
        var pcm = new byte[640];
        for (var i = 0; i < pcm.Length; i += 2)
            BitConverter.TryWriteBytes(pcm.AsSpan(i), (short)-1234);
        Assert.Equal(1234, PcmMath.Rms(pcm), precision: 6);
    }

    [Fact]
    public void Rms_IgnoresOddTrailingByte()
    {
        var pcm = new byte[3];
        BitConverter.TryWriteBytes(pcm.AsSpan(0), (short)100);
        pcm[2] = 0xFF;
        Assert.Equal(100, PcmMath.Rms(pcm), precision: 6);
    }
}
