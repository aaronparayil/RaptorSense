using System.Globalization;

namespace RaptorSense.Hardware;

/// <summary>The battery on whichever system this is.</summary>
public static class Battery
{
    /// <summary>The first battery's wear data, or null if there is no battery or it can't be read.</summary>
    public static BatteryInfo? TryRead() => OperatingSystem.IsLinux() ? LinuxBattery.TryRead() : WindowsBattery.TryRead();
}

/// <summary>Reads the battery from the files Linux keeps under <c>/sys/class/power_supply</c>. No special rights needed.</summary>
public static class LinuxBattery
{
    public const string SysRoot = "/sys/class/power_supply";

    /// <summary>The laptop's own battery, or null if there is none. A mouse's or headset's battery doesn't count.</summary>
    public static BatteryInfo? TryRead(string root = SysRoot)
    {
        if (!Directory.Exists(root)) return null;
        foreach (var dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            try
            {
                if (Text(dir, "type") != "Battery" || Text(dir, "scope") == "Device") continue;

                // Most laptops report energy (µWh). Some report charge (µAh) instead, which needs the design voltage (µV).
                long? design = Number(dir, "energy_full_design"), full = Number(dir, "energy_full");
                if (design is null && Number(dir, "voltage_min_design") is long microvolts and > 0)
                {
                    design = Number(dir, "charge_full_design") * microvolts / 1_000_000;
                    full = Number(dir, "charge_full") * microvolts / 1_000_000;
                }
                return new BatteryInfo(Known(Number(dir, "cycle_count")), Known(design / 1000), Known(full / 1000));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // went away while we looked
        }
        return null;
    }

    static string? Text(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    static long? Number(string dir, string name) =>
        long.TryParse(Text(dir, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    // The kernel writes 0 for what the battery doesn't tell it.
    static int? Known(long? value) => value is > 0 and <= int.MaxValue ? (int)value : null;
}
