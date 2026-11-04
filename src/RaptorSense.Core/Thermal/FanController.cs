namespace RaptorSense.Thermal;

/// <summary>
/// Owns the fans and enforces the safety rules. Thread-safe; <see cref="Tick"/> runs once a second.
/// <list type="bullet">
/// <item>Custom speeds never go below <see cref="MinCustomPercent"/>.</item>
/// <item>At <see cref="HotC"/> °C in custom mode the fans go to Max until everything is below <see cref="CoolC"/> °C.</item>
/// <item>If sensors cannot be read in custom mode, the fans go to Max.</item>
/// <item>If the firmware drops a setting (it does after sleep), it is applied again.</item>
/// <item><see cref="Release"/> hands the fans back to Auto when RaptorSense stops.</item>
/// </list>
/// </summary>
public sealed class FanController(IGamingFirmware firmware)
{
    public const int MinCustomPercent = 25;
    public const int HotC = 90, CoolC = 80;
    const int FailuresBeforeMax = 3;
    const int VerifyEveryTicks = 5;

    readonly object sync = new();
    bool changedByUs;
    int failures, ticks;

    public FanMode RequestedMode { get; private set; } = FanMode.Auto;
    public int CpuPercent { get; private set; } = 50;
    public int GpuPercent { get; private set; } = 50;

    /// <summary>True while the safety rules hold the fans at Max instead of the requested custom speeds.</summary>
    public bool SafetyOverride { get; private set; }

    public ThermalReading? LastReading { get; private set; }

    public string? LastError { get; private set; }

    public void SetAuto() => Request(FanMode.Auto, CpuPercent, GpuPercent);

    public void SetMax() => Request(FanMode.Max, CpuPercent, GpuPercent);

    public void SetCustom(int cpuPercent, int gpuPercent) => Request(FanMode.Custom, cpuPercent, gpuPercent);

    void Request(FanMode mode, int cpu, int gpu)
    {
        lock (sync)
        {
            RequestedMode = mode;
            CpuPercent = Math.Clamp(cpu, MinCustomPercent, 100);
            GpuPercent = Math.Clamp(gpu, MinCustomPercent, 100);
            changedByUs = mode != FanMode.Auto || changedByUs;

            // Don't lower the fans while it is still hot: the override stays until it cools.
            if (mode == FanMode.Custom && SafetyOverride) return;
            SafetyOverride = false;
            Apply();
        }
    }

    public void Tick()
    {
        lock (sync)
        {
            ThermalReading reading;
            try
            {
                reading = firmware.ReadSensors();
                LastReading = reading;
                failures = 0;
                if (!SafetyOverride) LastError = null;   // a passing glitch shouldn't be reported forever
            }
            catch (Exception e) when (e is FirmwareException or System.Runtime.InteropServices.ExternalException)
            {
                LastError = $"Could not read sensors: {e.Message}";
                if (++failures >= FailuresBeforeMax && RequestedMode == FanMode.Custom && !SafetyOverride) ForceMax();
                return;
            }

            if (RequestedMode == FanMode.Custom)
            {
                if (!SafetyOverride && reading.Hottest >= HotC) ForceMax();
                else if (SafetyOverride && reading.Hottest < CoolC && failures == 0)
                {
                    SafetyOverride = false;
                    Apply();
                }
            }

            // Re-apply when the firmware has reset the fans, e.g. after sleep.
            if (++ticks % VerifyEveryTicks == 0 && changedByUs)
            {
                var expected = SafetyOverride ? FanMode.Max : RequestedMode;
                var (cpu, gpu) = firmware.GetFanModes();
                if (cpu != expected || gpu != expected)
                {
                    if (SafetyOverride) firmware.SetFanMode(FanMode.Max);
                    else Apply();
                }
            }
        }
    }

    /// <summary>
    /// Call once at start. If a previous run crashed with the fans on Custom, nothing reset them, so do it now.
    /// Max (perhaps set elsewhere) is left alone: it is always safe.
    /// </summary>
    public void RecoverOnStart()
    {
        lock (sync)
        {
            var (cpu, gpu) = firmware.GetFanModes();
            if (cpu == FanMode.Custom || gpu == FanMode.Custom) firmware.SetFanMode(FanMode.Auto);
        }
    }

    /// <summary>Hands the fans back to the firmware's automatic control, if RaptorSense ever changed them.</summary>
    public void Release()
    {
        lock (sync)
        {
            if (!changedByUs) return;
            try { firmware.SetFanMode(FanMode.Auto); }
            catch (FirmwareException e) { LastError = e.Message; }
            RequestedMode = FanMode.Auto;
            SafetyOverride = false;
            changedByUs = false;
        }
    }

    void ForceMax()
    {
        SafetyOverride = true;
        LastError = $"Safety: fans at Max until below {CoolC} °C.";
        firmware.SetFanMode(FanMode.Max);
    }

    void Apply()
    {
        if (RequestedMode == FanMode.Custom) firmware.SetCustomFanSpeeds(CpuPercent, GpuPercent);
        else firmware.SetFanMode(RequestedMode);
        LastError = null;
    }
}
