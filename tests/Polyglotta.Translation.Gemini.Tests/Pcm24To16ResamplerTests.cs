using Polyglotta.Core.Audio;
using Polyglotta.Translation.Gemini;
using Xunit;

namespace Polyglotta.Translation.Gemini.Tests;

/// <summary>
/// Conversão da saída do Gemini (24 kHz) para o formato canônico (16 kHz).
/// Lógica pura: nenhuma chamada ao serviço.
/// </summary>
public class Pcm24To16ResamplerTests
{
    private const int SourceRate = Pcm24To16Resampler.SourceSampleRate;

    /// <summary>Senoide de 16-bit a 24 kHz, com a duração pedida.</summary>
    private static byte[] Sine(double seconds, double frequency = 440, double amplitude = 0.5)
    {
        var samples = (int)(SourceRate * seconds);
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(amplitude * short.MaxValue * Math.Sin(2 * Math.PI * frequency * i / SourceRate));
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), value);
        }
        return pcm;
    }

    private static int PeakAmplitude(byte[] pcm16) =>
        pcm16.Length < 2 ? 0 : Enumerable.Range(0, pcm16.Length / 2)
            .Select(i => Math.Abs((int)BitConverter.ToInt16(pcm16, i * 2))).Max();

    [Fact]
    public void Convert_ShrinksTheAudioByTwoThirds()
    {
        var resampler = new Pcm24To16Resampler();

        // 300 ms a 24 kHz devem virar ~300 ms a 16 kHz, ou seja, 2/3 dos bytes.
        var output = resampler.Convert(Sine(0.3));

        var expected = (int)(AudioFrame.SampleRate * 0.3) * 2;
        Assert.InRange(output.Length, expected * 0.95, expected * 1.05);
    }

    [Fact]
    public void Convert_PreservesTheSignal()
    {
        var resampler = new Pcm24To16Resampler();
        var output = resampler.Convert(Sine(0.3, amplitude: 0.5));

        // 0,5 de fundo de escala ≈ 16384; o resampler não deve atenuar nem estourar.
        Assert.InRange(PeakAmplitude(output), 12_000, 18_000);
    }

    [Fact]
    public void Convert_KeepsLeftoverSamplesBetweenCalls()
    {
        var resampler = new Pcm24To16Resampler();
        var chunk = Sine(0.04); // 40 ms, tamanho típico de chunk do Gemini

        var total = 0;
        for (var i = 0; i < 10; i++)
            total += resampler.Convert(chunk).Length;

        // 400 ms de entrada ≈ 400 ms de saída; sem perder sobras nas emendas.
        var expected = (int)(AudioFrame.SampleRate * 0.4) * 2;
        Assert.InRange(total, expected * 0.95, expected * 1.05);
    }

    [Fact]
    public void Convert_OfSilence_StaysSilent()
    {
        var resampler = new Pcm24To16Resampler();
        var output = resampler.Convert(new byte[SourceRate * 2 / 10]); // 100 ms de zeros
        Assert.Equal(0, PeakAmplitude(output));
    }

    [Fact]
    public void Convert_OfNothing_ReturnsNothing() =>
        Assert.Empty(new Pcm24To16Resampler().Convert(ReadOnlySpan<byte>.Empty));

    [Fact]
    public void Convert_Terminates_OnARepeatedDrain()
    {
        // Regressão do ReadFully: com ele ligado, o laço de drenagem nunca terminaria.
        var resampler = new Pcm24To16Resampler();
        var task = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
                resampler.Convert(Sine(0.1));
        });
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "Convert deve terminar quando a entrada acaba");
    }
}
