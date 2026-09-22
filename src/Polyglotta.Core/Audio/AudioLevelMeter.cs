namespace Polyglotta.Core.Audio;

/// <summary>Summary of the captured signal over one reporting window.</summary>
/// <param name="Frames">Frames seen in the window.</param>
/// <param name="VoicedFrames">Frames whose RMS was above the voice threshold.</param>
/// <param name="PeakRms">Loudest frame RMS, 0..32767.</param>
/// <param name="Window">Time spanned by the window, from frame timestamps.</param>
public readonly record struct AudioLevelReport(int Frames, int VoicedFrames, double PeakRms, TimeSpan Window)
{
    /// <summary>Peak as a fraction of full scale, handy for logging (0..1).</summary>
    public double PeakScale => PeakRms / (double)short.MaxValue;

    /// <summary>
    /// True when the window looks like a disconnected or muted input: audio frames keep
    /// arriving but carry nothing but the noise floor.
    /// </summary>
    public bool LooksSilent => Frames > 0 && PeakRms < 50;
}

/// <summary>
/// Accumulates capture levels and emits a report once per window, so the logs show
/// whether the microphone is actually picking anything up.
/// <para>
/// Windows are measured in audio time (sum of frame durations) rather than wall clock,
/// which keeps it deterministic in tests.
/// </para>
/// </summary>
public sealed class AudioLevelMeter
{
    private readonly TimeSpan _interval;
    private readonly int _voiceThresholdRms;
    private TimeSpan _window;
    private int _frames;
    private int _voiced;
    private double _peak;

    public AudioLevelMeter(TimeSpan? interval = null, int voiceThresholdRms = 500)
    {
        _interval = interval ?? TimeSpan.FromSeconds(5);
        _voiceThresholdRms = voiceThresholdRms;
    }

    public void Add(AudioFrame frame)
    {
        _frames++;
        _window += frame.Duration;
        var rms = PcmMath.Rms(frame.Pcm);
        if (rms > _peak) _peak = rms;
        if (rms >= _voiceThresholdRms) _voiced++;
    }

    /// <summary>Returns a report and starts a new window once the interval has elapsed.</summary>
    public AudioLevelReport? TryReport()
    {
        if (_frames == 0 || _window < _interval)
            return null;

        var report = new AudioLevelReport(_frames, _voiced, _peak, _window);
        _window = TimeSpan.Zero;
        _frames = 0;
        _voiced = 0;
        _peak = 0;
        return report;
    }
}
