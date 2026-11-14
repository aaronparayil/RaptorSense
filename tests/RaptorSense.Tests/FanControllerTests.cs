using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

public sealed class FanControllerTests
{
    sealed class FakeFirmware : IGamingFirmware
    {
        public int CpuTemp = 60, GpuTemp = 55;
        public bool SensorsFail;
        public FanMode Cpu = FanMode.Auto, Gpu = FanMode.Auto;
        public int CpuPercent, GpuPercent;
        public readonly List<string> Calls = [];

        public ThermalReading ReadSensors()
        {
            if (SensorsFail) throw new FirmwareException("sensor read failed");
            return new ThermalReading(CpuTemp, GpuTemp, 50, 3000, 3000);
        }

        public IReadOnlyList<PowerProfile> SupportedProfiles() => [PowerProfile.Quiet, PowerProfile.Balanced];
        public PowerProfile GetProfile() => PowerProfile.Balanced;
        public void SetProfile(PowerProfile profile) { }
        public int FanModeReads;

        public (FanMode Cpu, FanMode Gpu) GetFanModes()
        {
            FanModeReads++;
            return (Cpu, Gpu);
        }

        public void SetFanMode(FanMode mode)
        {
            Cpu = Gpu = mode;
            Calls.Add(mode.ToString());
        }

        public void SetCustomFanSpeeds(int cpuPercent, int gpuPercent)
        {
            Cpu = Gpu = FanMode.Custom;
            CpuPercent = cpuPercent;
            GpuPercent = gpuPercent;
            Calls.Add($"Custom {cpuPercent}/{gpuPercent}");
        }

        public void Dispose() { }
    }

    [Fact]
    public void Custom_speeds_never_go_below_the_minimum()
    {
        var fw = new FakeFirmware();
        var fans = new FanController(fw);
        fans.SetCustom(0, 10);
        Assert.Equal(FanController.MinCustomPercent, fw.CpuPercent);
        Assert.Equal(FanController.MinCustomPercent, fw.GpuPercent);
    }

    [Fact]
    public void Hot_in_custom_mode_forces_max_until_it_cools()
    {
        var fw = new FakeFirmware();
        var fans = new FanController(fw);
        fans.SetCustom(40, 40);

        fw.GpuTemp = FanController.HotC;
        fans.Tick();
        Assert.True(fans.SafetyOverride);
        Assert.Equal(FanMode.Max, fw.Cpu);

        fw.GpuTemp = FanController.CoolC + 5;   // cooler, but not cool enough yet
        fans.Tick();
        Assert.Equal(FanMode.Max, fw.Cpu);

        fw.GpuTemp = FanController.CoolC - 1;
        fans.Tick();
        Assert.False(fans.SafetyOverride);
        Assert.Equal(FanMode.Custom, fw.Cpu);
        Assert.Equal(40, fw.CpuPercent);
    }

    [Fact]
    public void Asking_for_custom_while_hot_does_not_lower_the_fans()
    {
        var fw = new FakeFirmware();
        var fans = new FanController(fw);
        fans.SetCustom(40, 40);
        fw.CpuTemp = 95;
        fans.Tick();

        fans.SetCustom(30, 30);
        Assert.Equal(FanMode.Max, fw.Cpu);
        Assert.Equal(30, fans.CpuPercent);   // remembered for when it cools
    }

    [Fact]
    public void Asking_for_auto_or_max_ends_the_override()
    {
        var fw = new FakeFirmware();
        var fans = new FanController(fw);
        fans.SetCustom(40, 40);
        fw.CpuTemp = 95;
        fans.Tick();

        fans.SetAuto();
        Assert.False(fans.SafetyOverride);
        Assert.Equal(FanMode.Auto, fw.Cpu);
    }

    [Fact]
    public void Repeated_sensor_failures_in_custom_mode_force_max()
    {
        var fw = new FakeFirmware();
        var fans = new FanController(fw);
        fans.SetCustom(40, 40);
        fw.SensorsFail = true;

        fans.Tick();
        fans.Tick();
        Assert.Equal(FanMode.Custom, fw.Cpu);   // one or two glitches are tolerated
        fans.Tick();
        Assert.Equal(FanMode.Max, fw.Cpu);
        Assert.True(fans.SafetyOverride);
    }

    [Fact]
    public void A_passing_sensor_glitch_stops_being_reported()
    {
        var fw = new FakeFirmware { SensorsFail = true };
        var fans = new FanController(fw);
        fans.Tick();
        Assert.NotNull(fans.LastError);

        fw.SensorsFail = false;
        fans.Tick();
        Assert.Null(fans.LastError);
    }

    [Fact]
    public void Heat_is_ignored_in_auto_mode()
    {
        var fw = new FakeFirmware();
        var fans = new FanController(fw);
        fw.CpuTemp = 95;
        fans.Tick();
        Assert.Empty(fw.Calls);   // the firmware's own curve is in charge
    }

    [Fact]
    public void A_setting_the_firmware_dropped_is_applied_again()
    {
        var fw = new FakeFirmware();
        var fans = new FanController(fw);
        fans.SetCustom(60, 70);
        fw.Cpu = fw.Gpu = FanMode.Auto;   // e.g. after sleep

        for (int i = 0; i < 5; i++) fans.Tick();
        Assert.Equal(FanMode.Custom, fw.Cpu);
        Assert.Equal(70, fw.GpuPercent);
    }

    [Fact]
    public void The_firmware_is_not_asked_about_the_fans_while_auto_is_wanted()
    {
        var firmware = new FakeFirmware();
        var fans = new FanController(firmware);
        for (int i = 0; i < 20; i++) fans.Tick();
        Assert.Equal(0, firmware.FanModeReads);   // never changed by us

        fans.SetMax();
        for (int i = 0; i < 20; i++) fans.Tick();
        int whileMax = firmware.FanModeReads;
        Assert.InRange(whileMax, 3, 5);   // every fifth check while it has something to keep in place

        fans.SetAuto();
        for (int i = 0; i < 20; i++) fans.Tick();
        Assert.Equal(whileMax, firmware.FanModeReads);   // back on Auto: asking costs a stall and changes nothing
    }

    [Fact]
    public void Release_returns_the_fans_to_auto_only_if_we_changed_them()
    {
        var untouched = new FakeFirmware();
        new FanController(untouched).Release();
        Assert.Empty(untouched.Calls);

        var fw = new FakeFirmware();
        var fans = new FanController(fw);
        fans.SetMax();
        fans.Release();
        Assert.Equal(FanMode.Auto, fw.Cpu);
    }

    [Fact]
    public void Start_after_a_crash_resets_leftover_custom_fans()
    {
        var fw = new FakeFirmware { Cpu = FanMode.Custom, Gpu = FanMode.Custom };
        new FanController(fw).RecoverOnStart();
        Assert.Equal(FanMode.Auto, fw.Cpu);

        var max = new FakeFirmware { Cpu = FanMode.Max, Gpu = FanMode.Max };
        new FanController(max).RecoverOnStart();
        Assert.Equal(FanMode.Max, max.Cpu);   // Max is safe, leave it
    }
}
