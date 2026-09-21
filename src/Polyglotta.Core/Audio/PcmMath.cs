namespace Polyglotta.Core.Audio;

/// <summary>Small helpers over canonical PCM (16-bit little-endian mono).</summary>
public static class PcmMath
{
    /// <summary>Root-mean-square amplitude of 16-bit samples, 0..32767. Odd trailing byte is ignored.</summary>
    public static double Rms(ReadOnlySpan<byte> pcm16)
    {
        var samples = pcm16.Length / 2;
        if (samples == 0)
            return 0;

        double sumSquares = 0;
        for (var i = 0; i < samples; i++)
        {
            double s = BitConverter.ToInt16(pcm16.Slice(i * 2, 2));
            sumSquares += s * s;
        }
        return Math.Sqrt(sumSquares / samples);
    }
}
