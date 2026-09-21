using Polyglotta.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Polyglotta.Audio.Windows;

/// <summary>
/// Converts device-native buffers (e.g. 48 kHz stereo IEEE float) to the
/// canonical pipeline format (16 kHz/16-bit/mono). Edge conversion per ADR-0004.
/// </summary>
internal sealed class CanonicalFormatConverter
{
    private readonly BufferedWaveProvider _input;
    private readonly IWaveProvider _output;
    private readonly byte[] _readBuffer = new byte[AudioFrame.BytesPer20Ms * 8];

    public CanonicalFormatConverter(WaveFormat nativeFormat)
    {
        _input = new BufferedWaveProvider(nativeFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2)
        };

        ISampleProvider sample = _input.ToSampleProvider();
        if (nativeFormat.Channels > 1)
            sample = new StereoToMonoSampleProvider(sample) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (nativeFormat.SampleRate != AudioFrame.SampleRate)
            sample = new WdlResamplingSampleProvider(sample, AudioFrame.SampleRate);

        _output = sample.ToWaveProvider16();
    }

    /// <summary>Feed native bytes; returns all complete canonical PCM currently available.</summary>
    public byte[] Convert(byte[] buffer, int bytes)
    {
        _input.AddSamples(buffer, 0, bytes);
        using var ms = new MemoryStream();
        int read;
        while ((read = _output.Read(_readBuffer, 0, _readBuffer.Length)) > 0)
        {
            ms.Write(_readBuffer, 0, read);
            if (read < _readBuffer.Length) break;
        }
        return ms.ToArray();
    }
}
