using Polyglotta.Core.Audio;

namespace Polyglotta.Core.Pipeline;

/// <summary>
/// Measures end-to-end latency as defined in PRD-001: end of the spoken phrase →
/// first synthesized audio of its translation.
/// <para>
/// "End of phrase" is the timestamp of the last voiced frame (RMS above
/// <see cref="VoiceThresholdRms"/>) seen when the final transcript arrives — the
/// provider only finalizes after its own end-of-speech detection, so at that moment the
/// last voiced frame is the phrase's tail. The next synthesized chunk closes the sample.
/// If the speaker has already started the next phrase, the sample is slightly
/// underestimated; acceptable for a p95 target of 3 s.
/// </para>
/// Thread-safe: frames arrive from the pump task, transcript/synthesis from provider threads.
/// </summary>
public sealed class LatencyTracker
{
    private readonly Lock _lock = new();
    private readonly List<double> _samplesMs = [];
    private DateTimeOffset? _lastVoicedAt;
    private DateTimeOffset? _phraseEndedAt;

    /// <summary>RMS (0..32767) above which a frame counts as speech. 500 ≈ -36 dBFS.</summary>
    public int VoiceThresholdRms { get; init; } = 500;

    public int Count { get { lock (_lock) return _samplesMs.Count; } }
    public TimeSpan? Last { get { lock (_lock) return _samplesMs.Count == 0 ? null : TimeSpan.FromMilliseconds(_samplesMs[^1]); } }

    /// <summary>Nearest-rank 95th percentile over all samples so far.</summary>
    public TimeSpan? P95
    {
        get
        {
            lock (_lock)
            {
                if (_samplesMs.Count == 0) return null;
                var sorted = _samplesMs.Order().ToArray();
                var rank = (int)Math.Ceiling(0.95 * sorted.Length) - 1;
                return TimeSpan.FromMilliseconds(sorted[Math.Clamp(rank, 0, sorted.Length - 1)]);
            }
        }
    }

    public void OnFrame(AudioFrame frame)
    {
        if (PcmMath.Rms(frame.Pcm) < VoiceThresholdRms)
            return;
        lock (_lock) _lastVoicedAt = frame.CapturedAt;
    }

    /// <summary>Call when the provider finalizes a phrase. Arms the next synthesis as the sample end.</summary>
    public void OnFinalTranscript()
    {
        lock (_lock)
        {
            if (_lastVoicedAt is { } t)
                _phraseEndedAt = t;
        }
    }

    /// <summary>
    /// Call for every synthesized chunk. Returns the latency sample when this chunk
    /// closes an armed phrase, null otherwise (continuation chunks, or nothing armed).
    /// </summary>
    public TimeSpan? OnSynthesizedAudio(DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_phraseEndedAt is not { } start)
                return null;
            _phraseEndedAt = null;
            var latency = now - start;
            _samplesMs.Add(latency.TotalMilliseconds);
            return latency;
        }
    }
}
