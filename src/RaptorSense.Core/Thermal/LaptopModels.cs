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

    public static bool IsVerified(string? model) =>
        !string.IsNullOrWhiteSpace(model) &&
        model.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => Verified.Contains(word, StringComparer.OrdinalIgnoreCase));

    public static string Describe(string? model) => string.IsNullOrWhiteSpace(model) ? "model unknown" : model.Trim();

    /// <summary>What the helper answers to a change it won't make on this model.</summary>
    public static string Refusal(string? model) =>
        $"RaptorSense hasn't been tested on this laptop ({Describe(model)}), so it doesn't change the fans, power " +
        "mode or light bar here. To help add it, see \"Other laptops\" in the README.";

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
