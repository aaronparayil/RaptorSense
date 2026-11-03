namespace RaptorSense.Thermal;

/// <summary>Power profiles, with the firmware's values (misc setting 0x0B).</summary>
public enum PowerProfile : byte
{
    Quiet = 0, Balanced = 1, Performance = 4, Turbo = 5, Eco = 6,
}

/// <summary>Fan behaviour, with the firmware's values.</summary>
public enum FanMode : byte
{
    Auto = 1, Max = 2, Custom = 3,
}

public readonly record struct ThermalReading(int CpuTemp, int GpuTemp, int SystemTemp, int CpuFanRpm, int GpuFanRpm)
{
    public int Hottest => Math.Max(CpuTemp, GpuTemp);
}

/// <summary>The laptop's gaming firmware: sensors, power profile and fans.</summary>
public interface IGamingFirmware : IDisposable
{
    ThermalReading ReadSensors();

    IReadOnlyList<PowerProfile> SupportedProfiles();

    PowerProfile GetProfile();

    void SetProfile(PowerProfile profile);

    (FanMode Cpu, FanMode Gpu) GetFanModes();

    /// <summary>Auto or Max for both fans.</summary>
    void SetFanMode(FanMode mode);

    /// <summary>Custom mode for both fans at these duty cycles (percent).</summary>
    void SetCustomFanSpeeds(int cpuPercent, int gpuPercent);
}

public sealed class FirmwareException(string message) : Exception(message);
