using RaptorSense.Thermal;

namespace RaptorSense.Automation;

public enum AutomationTrigger { AppOpen, OnBattery, PluggedIn }

/// <summary>"While this is true, use these settings." A null setting means the rule leaves it alone.</summary>
public sealed record AutomationRule
{
    public bool Enabled { get; init; } = true;

    public AutomationTrigger Trigger { get; init; } = AutomationTrigger.AppOpen;

    /// <summary>The program's name, with or without ".exe". Only used by <see cref="AutomationTrigger.AppOpen"/>.</summary>
    public string App { get; init; } = "";

    /// <summary>Name of a saved lighting profile.</summary>
    public string? LightingProfile { get; init; }

    public PowerProfile? Power { get; init; }

    public FanMode? Fans { get; init; }

    /// <summary>The name Windows knows the program by: no spaces around it and no ".exe".</summary>
    public string AppName
    {
        get
        {
            var name = App.Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }
    }

    public bool IsActive(AutomationState state) => Enabled && Trigger switch
    {
        AutomationTrigger.AppOpen => AppName.Length > 0 && state.OpenApps.Contains(AppName),
        AutomationTrigger.OnBattery => state.OnBattery,
        AutomationTrigger.PluggedIn => !state.OnBattery,
        _ => false,
    };
}

/// <summary>What the rules look at. <see cref="OpenApps"/> must compare names ignoring case.</summary>
public sealed record AutomationState(IReadOnlySet<string> OpenApps, bool OnBattery);

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
