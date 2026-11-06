using RaptorSense.Profiles;
using RaptorSense.Thermal;

namespace RaptorSense.Automation;

/// <summary>What the rules act on: the app's lighting and the elevated helper.</summary>
public interface IAutomationTarget
{
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
            while (applied != desired)
            {
                var next = desired;
                LastProblem = null;
                ApplyLighting(next.LightingProfile);
                await ApplyPowerAsync(next.Power);
                await ApplyFansAsync(next.Fans);
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
        await ApplyPowerAsync(null);
        await ApplyFansAsync(null);
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

        if (target.LoadProfile(wanted) is not { } profile)
        {
            LastProblem = $"The profile '{wanted}' no longer exists.";
            return;
        }
        LightingBefore ??= target.CaptureLighting();
        target.ApplyLighting(profile);
    }

    async Task ApplyPowerAsync(PowerProfile? wanted)
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
            if (powerBefore is { } before) Report(await target.SetPowerAsync(before));
            powerBefore = null;
        }
    }

    async Task ApplyFansAsync(FanMode? wanted)
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
            if (fansBefore is { } before) Report(await target.SetFansAsync(before.Mode, before.Cpu, before.Gpu));
            fansBefore = null;
        }
    }

    void Report(ThermalStatus status)
    {
        if (!status.Ok) LastProblem = status.Error ?? ThermalClient.HelperMissing;
    }
}
