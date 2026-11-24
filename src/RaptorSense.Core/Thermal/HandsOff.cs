namespace RaptorSense.Thermal;

/// <summary>Who controls the fans, the power mode and the PREDATOR key.</summary>
public enum HandsOffChoice
{
    /// <summary>RaptorSense, except while another app that controls them is running.</summary>
    Automatic,

    /// <summary>Always RaptorSense.</summary>
    Never,

    /// <summary>Always another app: RaptorSense does lighting only.</summary>
    Always,
}

/// <summary>
/// "Lighting only": RaptorSense leaves the fans, the power mode and the PREDATOR key alone, so it can run beside an app
/// such as PreySense that controls them. Two apps setting the fans would undo each other.
/// </summary>
/// <param name="isRunning">Whether such an app is running. Asked at most once per <see cref="MaxAge"/>, and only when something needs to know.</param>
/// <param name="now">A clock in milliseconds.</param>
public sealed class HandsOff(Func<bool> isRunning, Func<long> now)
{
    /// <summary>Programs that control the fans and power mode themselves, as Windows names their processes.</summary>
    public static readonly IReadOnlyList<string> OtherApps = ["PreySense"];

    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(3);

    readonly Lock gate = new();
    bool running;
    long? checkedAt;

    public HandsOffChoice Choice { get; set; }

    /// <summary>True when RaptorSense should leave the fans, the power mode and the PREDATOR key alone right now.</summary>
    public bool Active => Choice switch
    {
        HandsOffChoice.Always => true,
        HandsOffChoice.Never => false,
        _ => OtherAppRunning,
    };

    bool OtherAppRunning
    {
        get
        {
            lock (gate)
            {
                long time = now();
                if (checkedAt is { } last && time - last < MaxAge.TotalMilliseconds) return running;
                checkedAt = time;
                return running = isRunning();
            }
        }
    }

    /// <summary>The saved choice; anything else, such as a hand-edited word, is <see cref="HandsOffChoice.Automatic"/>.</summary>
    public static HandsOffChoice Parse(string? saved) => saved switch
    {
        "raptorsense" => HandsOffChoice.Never,
        "other" => HandsOffChoice.Always,
        _ => HandsOffChoice.Automatic,
    };

    /// <summary>How the choice is saved; null for the one that needs no saving.</summary>
    public static string? Save(HandsOffChoice choice) => choice switch
    {
        HandsOffChoice.Never => "raptorsense",
        HandsOffChoice.Always => "other",
        _ => null,
    };
}
