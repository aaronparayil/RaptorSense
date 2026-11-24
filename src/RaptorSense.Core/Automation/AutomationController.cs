using RaptorSense.Profiles;
using RaptorSense.Thermal;

namespace RaptorSense.Automation;

/// <summary>What the rules act on: the app's lighting and the elevated helper.</summary>
public interface IAutomationTarget
{
    /// <summary>True while another app has the fans and the power mode, so rules change only the lighting.</summary>
    bool LeavesFansAndPower { get; }

    Profile CaptureLighting();

    /// <summary>The saved profile with this name, or null if it no longer exists.</summary>
    Profile? LoadProfile(string name);

    void ApplyLighting(Profile profile);

    Task<ThermalStatus> GetStatusAsync();

    Task<ThermalStatus> SetPowerAsync(PowerProfile profile);

    Task<ThermalStatus> SetFansAsync(FanMode mode, int cpuPercent, int gpuPercent);
}

/// <summary>
/// Moves the laptop to what the rules want. When a rule first takes over a setting it remembers the old value, and puts
/// it back when no rule wants that setting any more. Call it from one thread. A failure is reported, never thrown, so
/// lighting rules keep working when the helper isn't installed.
/// </summary>
public sealed class AutomationController(IAutomationTarget target)
{
    const int DefaultFanPercent = 50;   // only used if a hand-edited rule asks for Custom

    /// <summary>Every key dark. The brightness and the light bar are left as they are.</summary>
    static readonly Profile LightsOff = new() { Name = AutomationRule.LightsOff, Effect = "static", Color = "000000" };

    AutomationOutcome desired, applied;
    bool running;
    PowerProfile? powerBefore;
    (FanMode Mode, int Cpu, int Gpu)? fansBefore;

    /// <summary>The lighting to return to while a rule is showing its own, or null. This is what should be saved as the user's setup.</summary>
    public Profile? LightingBefore { get; private set; }

    /// <summary>Why the last change didn't fully work, or null.</summary>
    public string? LastProblem { get; private set; }

    /// <summary>Raised after each change has been applied.</summary>
    public event Action? Applied;

    /// <summary>Applies the outcome. If a change is still in progress it is applied right after, so the latest one always wins.</summary>
    public async Task RequestAsync(AutomationOutcome outcome)
    {
        desired = outcome;
        if (running) return;
        running = true;
        try
        {
            while (true)
            {
                bool handsOff = target.LeavesFansAndPower;
                var next = handsOff ? desired with { Power = null, Fans = null } : desired;
                if (applied == next) break;
                LastProblem = null;
                ApplyLighting(next.LightingProfile);
                await ApplyPowerAsync(next.Power, handsOff);
                await ApplyFansAsync(next.Fans, handsOff);
                applied = next;
                Applied?.Invoke();
            }
        }
        finally { running = false; }
    }

    /// <summary>
    /// Puts the power mode and fans back when the app quits. They would otherwise stay as a rule set them; the lighting
    /// stops with the app, and <see cref="LightingBefore"/> is what gets saved.
    /// </summary>
    public async Task RestoreHelperSettingsAsync()
    {
        bool handsOff = target.LeavesFansAndPower;
        await ApplyPowerAsync(null, handsOff);
        await ApplyFansAsync(null, handsOff);
        applied = applied with { Power = null, Fans = null };
    }

    void ApplyLighting(string? wanted)
    {
        if (wanted == applied.LightingProfile) return;
        if (wanted is null)
        {
            if (LightingBefore is { } before) target.ApplyLighting(before);
            LightingBefore = null;
            return;
        }

        if ((target.LoadProfile(wanted) ?? (wanted == AutomationRule.LightsOff ? LightsOff : null)) is not { } profile)
        {
            LastProblem = $"The profile '{wanted}' no longer exists.";
            return;
        }
        LightingBefore ??= target.CaptureLighting();
        target.ApplyLighting(profile);
    }

    async Task ApplyPowerAsync(PowerProfile? wanted, bool handsOff)
    {
        if (wanted == applied.Power) return;
        if (wanted is { } profile)
        {
            if (applied.Power is null)
            {
                var status = await target.GetStatusAsync();
                powerBefore = status.Ok ? status.Profile : null;
            }
            Report(await target.SetPowerAsync(profile));
        }
        else
        {
            // Once another app has the power mode, the old one is not ours to bring back.
            if (!handsOff && powerBefore is { } before) Report(await target.SetPowerAsync(before));
            powerBefore = null;
        }
    }

    async Task ApplyFansAsync(FanMode? wanted, bool handsOff)
    {
        if (wanted == applied.Fans) return;
        if (wanted is { } mode)
        {
            if (applied.Fans is null)
            {
                var status = await target.GetStatusAsync();
                fansBefore = status.Ok ? (status.FanMode, status.CpuPercent, status.GpuPercent) : null;
            }
            Report(await target.SetFansAsync(mode, DefaultFanPercent, DefaultFanPercent));
        }
        else
        {
            // With another app in charge a rule's fan setting is let go of, not swapped for an older one of ours: the
            // firmware's own control is the safe thing to leave behind.
            if (handsOff) Report(await target.SetFansAsync(FanMode.Auto, DefaultFanPercent, DefaultFanPercent));
            else if (fansBefore is { } before) Report(await target.SetFansAsync(before.Mode, before.Cpu, before.Gpu));
            fansBefore = null;
        }
    }

    void Report(ThermalStatus status)
    {
        if (!status.Ok) LastProblem = status.Error ?? ThermalClient.HelperMissing;
    }
}
