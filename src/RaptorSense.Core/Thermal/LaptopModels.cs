using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RaptorSense.Thermal;

/// <summary>
/// The laptops whose fan, power and light bar commands have been checked on the real machine. The helper changes
/// nothing on any other model. Acer's gaming laptops share the name of the firmware interface, but what its values
/// mean can differ between models, and a wrong fan command is the one way RaptorSense could harm a laptop. On other
/// models the helper only reads.
/// </summary>
public static class LaptopModels
{
    /// <summary>Model codes as one word of what Windows reports (Win32_ComputerSystem.Model, e.g. "Predator PH16-71").</summary>
    public static readonly IReadOnlyList<string> Verified = ["PH16-71"];

    /// <summary>
    /// Laptops whose 4-zone keyboard has been checked on the real machine. Only the keyboard: on these the helper changes
    /// the keyboard's colours and nothing else unless the model is also in <see cref="Verified"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> FourZoneKeyboards = ["AN515-57"];

    /// <summary>
    /// Laptops whose fan commands (Auto, Max, Custom speeds) have been checked on the real machine, as well as every
    /// <see cref="Verified"/> one. The AN515-57 was checked on 2026-11-16: each mode read back and the fans followed.
    /// On these the helper changes the fans (and runs the fan safety rules) even where power modes stay untouched.
    /// </summary>
    public static readonly IReadOnlyList<string> FanControl = ["AN515-57"];

    public static bool IsVerified(string? model) => Matches(model, Verified);

    public static bool HasFourZoneKeyboard(string? model) => Matches(model, FourZoneKeyboards);

    public static bool HasFanControl(string? model) => IsVerified(model) || Matches(model, FanControl);

    static bool Matches(string? model, IReadOnlyList<string> codes) =>
        !string.IsNullOrWhiteSpace(model) &&
        model.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => codes.Contains(word, StringComparer.OrdinalIgnoreCase));

    public static string Describe(string? model) => string.IsNullOrWhiteSpace(model) ? "model unknown" : model.Trim();

    /// <summary>What the helper answers to a change it won't make on this model.</summary>
    public static string Refusal(string? model)
    {
        // Name only what is still off on this model, and what works anyway.
        var off = new List<string>();
        if (!HasFanControl(model)) off.Add("the fans");
        off.Add("the power mode");
        if (!HasFourZoneKeyboard(model)) off.Add("the light bar");   // the Nitro has none
        string what = off.Count == 1 ? off[0] : string.Join(", ", off.Take(off.Count - 1)) + " or " + off[^1];

        var works = new List<string>();
        if (HasFanControl(model)) works.Add("the fans");
        if (HasFourZoneKeyboard(model)) works.Add("the keyboard's colours");
        string still = works.Count == 0 ? "" : $" ({string.Join(" and ", works)} work)";

        return $"RaptorSense hasn't tested changing {what} on this laptop ({Describe(model)}), so it doesn't change " +
               (off.Count == 1 ? "it" : "them") + " here" + still + ". To help add it, see \"Other laptops\" in the README.";
    }

    public const string DmiRoot = "/sys/class/dmi/id";

    /// <summary>The model of the laptop this is running on, on whichever system, or null if it can't be read.</summary>
    public static string? ReadThisModel() =>
        OperatingSystem.IsWindows() ? ReadModel() : OperatingSystem.IsLinux() ? ReadModelFromDmi() : null;

    /// <summary>
    /// The model as Linux shows it in <c>/sys/class/dmi/id/product_name</c>: the same firmware field Windows reports
    /// (e.g. "Predator PH16-71"), readable without special rights. Null if it isn't there.
    /// </summary>
    public static string? ReadModelFromDmi(string root = DmiRoot)
    {
        try
        {
            var path = Path.Combine(root, "product_name");
            if (!File.Exists(path)) return null;
            var name = File.ReadAllText(path).Trim();
            return name.Length == 0 ? null : name;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The model as Windows reports it, or null if it can't be read (which counts as not verified).</summary>
    [SupportedOSPlatform("windows")]
    public static string? ReadModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            foreach (var found in searcher.Get())
            {
                using (found) return (found["Model"] as string)?.Trim();
            }
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException) { }
        return null;
    }
}

/// <summary>
/// Passes every read through and refuses every change, without passing it on. The helper uses it on a laptop that
/// isn't in <see cref="LaptopModels.Verified"/>, so nothing it runs, the fan safety rules included, can write.
/// </summary>
public sealed class ReadOnlyFirmware(IGamingFirmware inner, string refusal) : IGamingFirmware
{
    public ThermalReading ReadSensors() => inner.ReadSensors();

    public IReadOnlyList<PowerProfile> SupportedProfiles() => inner.SupportedProfiles();

    public PowerProfile GetProfile() => inner.GetProfile();

    public (FanMode Cpu, FanMode Gpu) GetFanModes() => inner.GetFanModes();

    public void SetProfile(PowerProfile profile) => throw new FirmwareException(refusal);

    public void SetFanMode(FanMode mode) => throw new FirmwareException(refusal);

    public void SetCustomFanSpeeds(int cpuPercent, int gpuPercent) => throw new FirmwareException(refusal);

    public void Dispose() { }   // the firmware belongs to whoever opened it
}

/// <summary>
/// Passes reads and fan changes through and refuses power mode changes, without passing them on. The helper uses it on
/// a laptop in <see cref="LaptopModels.FanControl"/> that isn't <see cref="LaptopModels.Verified"/>.
/// </summary>
public sealed class FansOnlyFirmware(IGamingFirmware inner, string refusal) : IGamingFirmware
{
    public ThermalReading ReadSensors() => inner.ReadSensors();

    public IReadOnlyList<PowerProfile> SupportedProfiles() => inner.SupportedProfiles();

    public PowerProfile GetProfile() => inner.GetProfile();

    public (FanMode Cpu, FanMode Gpu) GetFanModes() => inner.GetFanModes();

    public void SetProfile(PowerProfile profile) => throw new FirmwareException(refusal);

    public void SetFanMode(FanMode mode) => inner.SetFanMode(mode);

    public void SetCustomFanSpeeds(int cpuPercent, int gpuPercent) => inner.SetCustomFanSpeeds(cpuPercent, gpuPercent);

    public void Dispose() { }   // the firmware belongs to whoever opened it
}
