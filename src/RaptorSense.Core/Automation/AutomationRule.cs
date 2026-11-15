using System.Text.Json.Serialization;
using RaptorSense.Thermal;

namespace RaptorSense.Automation;

public enum AutomationTrigger { AppOpen, OnBattery, PluggedIn, SoundPlaying, Idle, ScreenOff }

/// <summary>"While this is true, use these settings." A null setting means the rule leaves it alone.</summary>
public sealed record AutomationRule
{
    /// <summary>
    /// A lighting choice that needs no saved profile: every key dark. A saved profile with the same name is used instead.
    /// </summary>
    public const string LightsOff = "Lights off";

    public const int MinIdleMinutes = 1, MaxIdleMinutes = 240, DefaultIdleMinutes = 5;

    public bool Enabled { get; init; } = true;

    public AutomationTrigger Trigger { get; init; } = AutomationTrigger.AppOpen;

    /// <summary>The program's name, with or without ".exe". Only used by <see cref="AutomationTrigger.AppOpen"/>.</summary>
    public string App { get; init; } = "";

    /// <summary>Minutes without a key press or mouse movement. Only used by <see cref="AutomationTrigger.Idle"/>; null means five.</summary>
    public int? IdleMinutes { get; init; }

    /// <summary>Name of a saved lighting profile, or <see cref="LightsOff"/>.</summary>
    public string? LightingProfile { get; init; }

    public PowerProfile? Power { get; init; }

    public FanMode? Fans { get; init; }

    /// <summary>The name Windows knows the program by: no spaces around it and no ".exe". Worked out from <see cref="App"/>, so not saved.</summary>
    [JsonIgnore]
    public string AppName
    {
        get
        {
            var name = App.Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }
    }

    /// <summary>How long nothing must be touched. Worked out from <see cref="IdleMinutes"/> and kept within sensible limits, so not saved.</summary>
    [JsonIgnore]
    public TimeSpan IdleAfter => TimeSpan.FromMinutes(Math.Clamp(IdleMinutes ?? DefaultIdleMinutes, MinIdleMinutes, MaxIdleMinutes));

    public bool IsActive(AutomationState state) => Enabled && Trigger switch
    {
        AutomationTrigger.AppOpen => AppName.Length > 0 && state.OpenApps.Contains(AppName),
        AutomationTrigger.OnBattery => state.OnBattery,
        AutomationTrigger.PluggedIn => !state.OnBattery,
        AutomationTrigger.SoundPlaying => state.SoundPlaying,
        AutomationTrigger.Idle => state.Idle >= IdleAfter,
        AutomationTrigger.ScreenOff => state.ScreenOff,
        _ => false,
    };
}

/// <summary>
/// What the rules look at. <see cref="OpenApps"/> must compare names ignoring case. <see cref="Idle"/> is how long
/// it has been since a key or the mouse was last used.
/// </summary>
public sealed record AutomationState(
    IReadOnlySet<string> OpenApps, bool OnBattery, bool SoundPlaying = false, TimeSpan Idle = default, bool ScreenOff = false);

/// <summary>What the rules want right now. A null setting means no rule wants it, so it goes back to how it was.</summary>
public readonly record struct AutomationOutcome(string? LightingProfile, PowerProfile? Power, FanMode? Fans)
{
    /// <summary>For each setting, the first active rule in the list that sets it wins.</summary>
    public static AutomationOutcome Decide(IEnumerable<AutomationRule> rules, AutomationState state)
    {
        var outcome = new AutomationOutcome();
        foreach (var rule in rules.Where(r => r.IsActive(state)))
        {
            outcome = new AutomationOutcome(
                outcome.LightingProfile ?? rule.LightingProfile,
                outcome.Power ?? rule.Power,
                outcome.Fans ?? rule.Fans);
        }
        return outcome;
    }
}
