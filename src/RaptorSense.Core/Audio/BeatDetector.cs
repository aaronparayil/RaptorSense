namespace RaptorSense.Audio;

/// <summary>
/// Spots beats in a level that is read once per frame (the bass level works well). A beat is the level jumping
/// clearly above its own recent average; it has to fall back before the next one counts, so a long bass note is
/// one beat, not many.
/// </summary>
public sealed class BeatDetector
{
    /// <summary>Shortest time between two beats (300 a minute).</summary>
    public const double MinGapSeconds = 0.2;

    const double AverageSeconds = 1.0, Rise = 0.1, Rearm = 0.03, Floor = 0.2;

    double average, sinceBeat = MinGapSeconds;
    bool armed = true;

    /// <summary>Feed the newest level (0..1) and the seconds since the last call. True on the frame a beat lands.</summary>
    public bool Update(double level, double deltaSeconds)
    {
        if (!double.IsFinite(level) || !double.IsFinite(deltaSeconds) || deltaSeconds < 0) return false;
        sinceBeat += deltaSeconds;

        bool beat = armed && sinceBeat >= MinGapSeconds && level >= Floor && level > average + Rise;
        if (beat)
        {
            sinceBeat = 0;
            armed = false;
        }
        else if (level < average + Rearm)
        {
            armed = true;
        }

        // Compare against the level from before this frame, then let the average follow.
        average += (level - average) * (1 - Math.Exp(-deltaSeconds / AverageSeconds));
        return beat;
    }

    public void Reset()
    {
        average = 0;
        sinceBeat = MinGapSeconds;
        armed = true;
    }
}
