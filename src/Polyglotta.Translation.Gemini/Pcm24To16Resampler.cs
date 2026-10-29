using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Polyglotta.Core.Audio;

namespace Polyglotta.Translation.Gemini;

/// <summary>
/// Converte o áudio sintetizado pelo Gemini (PCM 16-bit, 24 kHz, mono) para o formato
/// canônico do pipeline (16 kHz) — conversão na borda do provedor, conforme ADR-0006.
/// <para>
/// Usa <c>NAudio.Core</c>, que é multiplataforma: o Core continua sem referência a NAudio
/// (ADR-0004) e este projeto não arrasta dependência de Windows.
/// </para>
/// <para>
/// Mantém estado entre chamadas (o resampler tem memória e sobras de amostras), então
/// cada sessão de tradução precisa da sua própria instância.
/// </para>
/// </summary>
internal sealed class Pcm24To16Resampler
{
    /// <summary>Taxa de amostragem da saída do Gemini Live.</summary>
    public const int SourceSampleRate = 24_000;

    private readonly BufferedWaveProvider _input;
    private readonly IWaveProvider _output;
    private readonly byte[] _scratch = new byte[AudioFrame.BytesPer20Ms * 16];

    public Pcm24To16Resampler()
    {
        var source = new WaveFormat(SourceSampleRate, AudioFrame.BitsPerSample, AudioFrame.Channels);

        // ReadFully desligado: com ele ligado, Read() preenche com silêncio indefinidamente
        // e o laço de drenagem nunca termina (mesma armadilha do CanonicalFramer).
        _input = new BufferedWaveProvider(source, TimeSpan.FromSeconds(10))
        {
            ReadFully = false,
            DiscardOnBufferOverflow = true
        };

        _output = new WdlResamplingSampleProvider(_input.ToSampleProvider(), AudioFrame.SampleRate)
            .ToWaveProvider16();
    }

    /// <summary>
    /// Converte um bloco de 24 kHz para 16 kHz. Sobras ficam guardadas para a próxima
    /// chamada, então a concatenação dos retornos é contínua, sem cliques nas emendas.
    /// </summary>
    public byte[] Convert(ReadOnlySpan<byte> pcm24)
    {
        if (pcm24.IsEmpty)
            return [];

        _input.AddSamples(pcm24);

        using var converted = new MemoryStream();
        int read;
        while ((read = _output.Read(_scratch)) > 0)
            converted.Write(_scratch, 0, read);

        return converted.ToArray();
    }
}
