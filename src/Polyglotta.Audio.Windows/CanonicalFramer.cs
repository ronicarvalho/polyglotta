using System.Threading.Channels;
using Polyglotta.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Polyglotta.Audio.Windows;

/// <summary>
/// Turns device-native capture buffers into canonical 20 ms <see cref="AudioFrame"/>s
/// (16 kHz/16-bit/mono, 640 bytes). Edge conversion per ADR-0004.
/// <para>
/// Captures request the canonical format from WASAPI (<c>WithFormat</c>, ADR-0005), so
/// the common path is pass-through slicing. When the engine hands us something else
/// (exclusive mode, older engines) we fall back to NAudio's downmix + WDL resampler.
/// </para>
/// Runs on the audio thread: no I/O, no locks, only small per-frame allocations.
/// </summary>
internal sealed class CanonicalFramer
{
    private readonly BufferedWaveProvider? _input;
    private readonly IWaveProvider? _output;
    private readonly byte[] _scratch = new byte[AudioFrame.BytesPer20Ms * 8];
    private byte[] _frame = new byte[AudioFrame.BytesPer20Ms];
    private int _frameFill;

    /// <summary>True when the native format already is canonical and no conversion runs.</summary>
    public bool IsPassThrough { get; }

    public CanonicalFramer(WaveFormat nativeFormat)
    {
        IsPassThrough = IsCanonical(nativeFormat);
        if (IsPassThrough)
            return;

        if (nativeFormat.Channels > 2)
            throw new NotSupportedException(
                $"Cannot downmix {nativeFormat.Channels} channels; request the canonical format from the device instead.");

        // ReadFully must be off: with it on, Read() pads with silence forever and the
        // drain loop in Feed() never terminates.
        _input = new BufferedWaveProvider(nativeFormat, TimeSpan.FromSeconds(2))
        {
            ReadFully = false,
            DiscardOnBufferOverflow = true
        };

        ISampleProvider sample = _input.ToSampleProvider();
        if (nativeFormat.Channels == 2)
            sample = new StereoToMonoSampleProvider(sample) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (nativeFormat.SampleRate != AudioFrame.SampleRate)
            sample = new WdlResamplingSampleProvider(sample, AudioFrame.SampleRate);

        _output = sample.ToWaveProvider16();
    }

    public static bool IsCanonical(WaveFormat f) =>
        f.Encoding == WaveFormatEncoding.Pcm &&
        f.SampleRate == AudioFrame.SampleRate &&
        f.BitsPerSample == AudioFrame.BitsPerSample &&
        f.Channels == AudioFrame.Channels;

    /// <summary>
    /// Feeds native bytes and writes every complete 20 ms frame to <paramref name="writer"/>.
    /// Leftover samples are kept for the next call. Never blocks (bounded channel, DropOldest).
    /// </summary>
    public void Feed(ReadOnlySpan<byte> native, ChannelWriter<AudioFrame> writer, DateTimeOffset capturedAt)
    {
        if (IsPassThrough)
        {
            Slice(native, writer, capturedAt);
            return;
        }

        _input!.AddSamples(native);
        int read;
        while ((read = _output!.Read(_scratch)) > 0)
            Slice(_scratch.AsSpan(0, read), writer, capturedAt);
    }

    private void Slice(ReadOnlySpan<byte> pcm, ChannelWriter<AudioFrame> writer, DateTimeOffset capturedAt)
    {
        while (!pcm.IsEmpty)
        {
            var take = Math.Min(pcm.Length, _frame.Length - _frameFill);
            pcm[..take].CopyTo(_frame.AsSpan(_frameFill));
            _frameFill += take;
            pcm = pcm[take..];

            if (_frameFill == _frame.Length)
            {
                writer.TryWrite(new AudioFrame(_frame, capturedAt));
                _frame = new byte[AudioFrame.BytesPer20Ms];
                _frameFill = 0;
            }
        }
    }
}
