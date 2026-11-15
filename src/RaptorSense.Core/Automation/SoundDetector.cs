namespace RaptorSense.Automation;

/// <summary>
/// Decides whether something is playing from a loudness reading taken a couple of times a second. A notification
/// chime is too short to count, and the gap between two songs is too short to end it.
/// </summary>
public sealed class SoundDetector
{
    /// <summary>A reading above this counts as sound. Full scale is 1; a paused player reads exactly 0.</summary>
    public const float Audible = 0.001f;

    /// <summary>Sound has to last this long before it counts as playing.</summary>
    public static readonly TimeSpan StartAfter = TimeSpan.FromSeconds(3);

    /// <summary>Silence has to last this long before it counts as stopped.</summary>
    public static readonly TimeSpan StopAfter = TimeSpan.FromSeconds(15);

    // A reading covers only a hundredth of a second, so it can land in the quiet between two drum hits.
    static readonly TimeSpan GapIgnored = TimeSpan.FromSeconds(1.5);

    TimeSpan heardFor, silentFor;

    public bool Playing { get; private set; }

    /// <summary>Takes one reading and the time since the last one. Returns true when <see cref="Playing"/> changed.</summary>
    public bool Update(float peak, TimeSpan elapsed)
    {
        if (peak > Audible)
        {
            heardFor += elapsed;
            silentFor = TimeSpan.Zero;
        }
        else
        {
            silentFor += elapsed;
            if (!Playing && silentFor >= GapIgnored) heardFor = TimeSpan.Zero;
        }

        bool playing = Playing ? silentFor < StopAfter : heardFor >= StartAfter;
        if (playing == Playing) return false;
        Playing = playing;
        heardFor = TimeSpan.Zero;
        return true;
    }

    public void Reset()
    {
        Playing = false;
        heardFor = silentFor = TimeSpan.Zero;
    }
}
