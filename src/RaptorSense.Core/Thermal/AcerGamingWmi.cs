using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RaptorSense.Hardware;
using RaptorSense.Lighting;

namespace RaptorSense.Thermal;

/// <summary>
/// The AcerGamingFunction WMI class (root\WMI). Needs administrator rights, even to read.
/// Encodings are in docs/protocol.md; each write is read back before it is reported as done
/// (the light bar's are not: nothing in the firmware reads a zone colour back reliably).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AcerGamingWmi : IGamingFirmware, ILightBar
{
    // The light bar only takes a colour change reliably when the whole round is sent a few times, 65 ms apart.
    const int LightBarRounds = 3;
    const int LightBarRoundGapMs = 65;

    const uint SensorCommand = 0x01;
    const byte CpuTempSensor = 0x01, CpuFanSensor = 0x02, SystemTempSensor = 0x03, GpuFanSensor = 0x06, GpuTempSensor = 0x0A;
    const uint SupportedProfilesSetting = 0x0A, ProfileSetting = 0x0B;
    const ulong BothFans = 0x9;           // CPU fan bit 0, GPU fan bit 3
    const ulong CpuFanId = 1, GpuFanId = 4;

    readonly ManagementObject instance;

    AcerGamingWmi(ManagementObject instance) => this.instance = instance;

    public static AcerGamingWmi Open()
    {
        using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM AcerGamingFunction");
        var instance = searcher.Get().Cast<ManagementObject>().FirstOrDefault()
            ?? throw new FirmwareException("AcerGamingFunction not found: this laptop may not be supported.");
        return new AcerGamingWmi(instance);
    }

    public ThermalReading ReadSensors() => new(
        Sensor(CpuTempSensor), Sensor(GpuTempSensor), Sensor(SystemTempSensor), Sensor(CpuFanSensor), Sensor(GpuFanSensor));

    public IReadOnlyList<PowerProfile> SupportedProfiles()
    {
        ulong mask = Value(Get("GetGamingMiscSetting", SupportedProfilesSetting));
        return Enum.GetValues<PowerProfile>().Where(p => (mask & (1UL << (int)p)) != 0).ToList();
    }

    public PowerProfile GetProfile() => (PowerProfile)Value(Get("GetGamingMiscSetting", ProfileSetting));

    public void SetProfile(PowerProfile profile)
    {
        if (!SupportedProfiles().Contains(profile)) throw new FirmwareException($"{profile} is not supported on this laptop.");

        // On Windows this call reports "Generic failure" even though the switch happens (confirmed on a PH16-71):
        // the error comes back with the reply, after the firmware has acted. So the read-back decides, not the reply.
        string? refused = null;
        try { Set("SetGamingMiscSetting", ProfileSetting | ((ulong)profile << 8)); }
        catch (FirmwareException e) { refused = e.Message; }
        Settle(() => GetProfile() == profile, refused ?? $"The firmware did not confirm the switch to {profile}.");
    }

    public (FanMode Cpu, FanMode Gpu) GetFanModes()
    {
        ulong output = Get("GetGamingFanBehavior", (uint)BothFans);
        return ((FanMode)((output >> 8) & 0x3), (FanMode)((output >> 14) & 0x3));
    }

    public void SetFanMode(FanMode mode)
    {
        if (mode is FanMode.Custom) throw new ArgumentException("Use SetCustomFanSpeeds for custom mode.");
        SetBehavior(mode);
    }

    public void SetCustomFanSpeeds(int cpuPercent, int gpuPercent)
    {
        SetBehavior(FanMode.Custom);
        Set("SetGamingFanSpeed", ((ulong)Math.Clamp(cpuPercent, 0, 100) << 8) | CpuFanId);
        Set("SetGamingFanSpeed", ((ulong)Math.Clamp(gpuPercent, 0, 100) << 8) | GpuFanId);
    }

    // CPU fan mode in bits 16-17, GPU fan mode in bits 22-23, fan mask in bits 0-15: Auto 0x410009, Max 0x820009, Custom 0xC30009.
    void SetBehavior(FanMode mode)
    {
        Set("SetGamingFanBehavior", BothFans | ((ulong)mode << 16) | ((ulong)mode << 22));
        Settle(() => GetFanModes() == (mode, mode), $"The firmware did not confirm the fans switching to {mode}.");
    }

    public void SetStatic(IReadOnlyList<Rgb> zones, int brightness)
    {
        if (zones.Count != LightBarProtocol.Zones) throw new ArgumentException($"The light bar has {LightBarProtocol.Zones} zones.", nameof(zones));
        CommitLightBar(LightBarProtocol.StaticBacklight(brightness), () =>
        {
            for (int zone = 0; zone < LightBarProtocol.Zones; zone++) Set("SetGamingRgbKb", LightBarProtocol.ZoneColor(zone, zones[zone]));
        });
    }

    public void SetMode(LightBarMode mode, Rgb color, int speed, int brightness) =>
        CommitLightBar(LightBarProtocol.ModeBacklight(mode, color, speed, brightness), null);

    void CommitLightBar(byte[] backlight, Action? zoneColors)
    {
        for (int round = 0; round < LightBarRounds; round++)
        {
            if (round > 0) Thread.Sleep(LightBarRoundGapMs);
            Set("SetGamingLED", LightBarProtocol.Trigger);
            Set("SetGamingKBBacklight", backlight);
            zoneColors?.Invoke();
        }
    }

    // The firmware is busy for a moment after a change and fails reads with "Generic failure" meanwhile,
    // so read back for up to ~1.5 s and tolerate errors until then (TrueSense found the same on Linux).
    static void Settle(Func<bool> confirmed, string failure)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            try { if (confirmed()) return; }
            catch (FirmwareException) { }
            Thread.Sleep(100);
        }
        throw new FirmwareException(failure);
    }

    int Sensor(byte id) => (int)((Get("GetGamingSysInfo", SensorCommand | ((uint)id << 8)) >> 8) & 0xFFFF);

    static ulong Value(ulong output) => (output >> 8) & 0xFF;

    ulong Get(string method, uint input)
    {
        ulong output = Invoke(method, input);
        if ((output & 0xFF) != 0) throw new FirmwareException($"{method}(0x{input:X}) failed with status {output & 0xFF}.");
        return output;
    }

    // input is a ulong or a byte[], whichever the method takes.
    void Set(string method, object input)
    {
        ulong status = Invoke(method, input) & 0xFF;
        if (status != 0) throw new FirmwareException($"{method}({Describe(input)}) failed with status {status}.");
    }

    static string Describe(object input) => input is byte[] bytes ? Convert.ToHexString(bytes) : $"0x{input:X}";

    // Every WMI failure leaves here as a FirmwareException: the fan safety rules count those as sensor failures.
    ulong Invoke(string method, object input)
    {
        try
        {
            using var parameters = instance.GetMethodParameters(method);
            parameters["gmInput"] = input;
            using var result = instance.InvokeMethod(method, parameters, null);
            return Convert.ToUInt64(result["gmOutput"]);
        }
        catch (Exception e) when (e is ManagementException or COMException)
        {
            throw new FirmwareException($"{method}({Describe(input)}) was refused: {e.Message.Trim()}");
        }
    }

    public void Dispose() => instance.Dispose();
}
