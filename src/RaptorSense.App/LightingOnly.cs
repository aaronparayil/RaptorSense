using System.Diagnostics;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>The app's <see cref="HandsOff"/>: the choice kept with the other settings, and the running programs.</summary>
static class LightingOnly
{
    const string Key = "FansAndPower";

    static readonly HandsOff handsOff = new(() => HandsOff.OtherApps.Any(IsRunning), () => Environment.TickCount64)
    {
        Choice = HandsOff.Parse(AppearanceSettings.Get(Key)),
    };

    /// <summary>True when another app has the fans, the power mode and the PREDATOR key.</summary>
    public static bool Active => handsOff.Active;

    public static HandsOffChoice Choice
    {
        get => handsOff.Choice;
        set
        {
            handsOff.Choice = value;
            AppearanceSettings.Set(Key, HandsOff.Save(value));
        }
    }

    static bool IsRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }
}
