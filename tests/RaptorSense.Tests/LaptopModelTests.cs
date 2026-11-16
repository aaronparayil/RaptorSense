using System.Text.Json;
using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

public sealed class LaptopModelTests
{
    sealed class RecordingFirmware : IGamingFirmware
    {
        public int Temp = 60;
        public FanMode Mode = FanMode.Auto;
        public readonly List<string> Writes = [];

        public ThermalReading ReadSensors() => new(Temp, Temp, 50, 3000, 3000);
        public IReadOnlyList<PowerProfile> SupportedProfiles() => [PowerProfile.Balanced, PowerProfile.Turbo];
        public PowerProfile GetProfile() => PowerProfile.Balanced;
        public (FanMode Cpu, FanMode Gpu) GetFanModes() => (Mode, Mode);
        public void SetProfile(PowerProfile profile) => Writes.Add($"profile {profile}");
        public void SetFanMode(FanMode mode) => Writes.Add($"fans {mode}");
        public void SetCustomFanSpeeds(int cpuPercent, int gpuPercent) => Writes.Add($"custom {cpuPercent}/{gpuPercent}");
        public void Dispose() { }
    }

    [Theory]
    [InlineData("Predator PH16-71")]
    [InlineData("  predator ph16-71 ")]
    [InlineData("PH16-71")]
    public void The_helios_16_is_verified(string model) => Assert.True(LaptopModels.IsVerified(model));

    [Theory]
    [InlineData("Nitro AN515-57")]
    [InlineData("Nitro AN515-58")]
    [InlineData("Predator PH18-71")]
    [InlineData("Predator PH16-71X")]
    [InlineData("Predator PH16-72")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Other_or_unknown_models_are_not(string? model) => Assert.False(LaptopModels.IsVerified(model));

    [Fact]
    public void The_refusal_names_the_model()
    {
        Assert.Contains("(Nitro AN515-57)", LaptopModels.Refusal(" Nitro AN515-57 "));
        Assert.Contains("(model unknown)", LaptopModels.Refusal(null));
    }

    [Theory]
    [InlineData("Predator PH16-71", true)]
    [InlineData("Nitro AN515-57", true)]
    [InlineData("Nitro AN515-58", false)]
    [InlineData("Predator PH18-71", false)]
    [InlineData(null, false)]
    public void Fan_control_only_on_checked_models(string? model, bool expected) =>
        Assert.Equal(expected, LaptopModels.HasFanControl(model));

    [Fact]
    public void The_refusal_lists_only_what_is_still_off()
    {
        var nitro = LaptopModels.Refusal("Nitro AN515-57");
        Assert.Contains("changing the power mode", nitro);
        Assert.DoesNotContain("the fans,", nitro);
        Assert.DoesNotContain("light bar", nitro);
        Assert.Contains("(the fans and the keyboard's colours work)", nitro);

        var unknown = LaptopModels.Refusal("Predator PH18-71");
        Assert.Contains("changing the fans, the power mode or the light bar", unknown);
        Assert.DoesNotContain("work)", unknown);
    }

    [Fact]
    public void Fans_only_firmware_changes_the_fans_but_not_the_power_mode()
    {
        var inner = new RecordingFirmware();
        var firmware = new FansOnlyFirmware(inner, "power modes aren't tested here");
        firmware.SetFanMode(FanMode.Max);
        firmware.SetCustomFanSpeeds(60, 55);
        Assert.Equal("power modes aren't tested here", Assert.Throws<FirmwareException>(() => firmware.SetProfile(PowerProfile.Turbo)).Message);
        Assert.Equal(["fans Max", "custom 60/55"], inner.Writes);
        Assert.Equal(71, new FansOnlyFirmware(new RecordingFirmware { Temp = 71 }, "no").ReadSensors().CpuTemp);
    }

    [Fact]
    public void The_fan_safety_rules_work_through_fans_only_firmware()
    {
        // As on the Nitro: custom speeds go to Max when it gets too hot.
        var inner = new RecordingFirmware();
        var fans = new FanController(new FansOnlyFirmware(inner, "no"));
        fans.SetCustom(40, 40);
        inner.Temp = FanController.HotC + 5;
        for (int tick = 0; tick < 5; tick++) fans.Tick();
        Assert.True(fans.SafetyOverride);
        Assert.Contains("fans Max", inner.Writes);
    }

    [Fact]
    public void Read_only_firmware_passes_reads_through()
    {
        var inner = new RecordingFirmware { Temp = 71, Mode = FanMode.Max };
        var firmware = new ReadOnlyFirmware(inner, "no");
        Assert.Equal(71, firmware.ReadSensors().CpuTemp);
        Assert.Equal(inner.SupportedProfiles(), firmware.SupportedProfiles());
        Assert.Equal(PowerProfile.Balanced, firmware.GetProfile());
        Assert.Equal((FanMode.Max, FanMode.Max), firmware.GetFanModes());
    }

    [Fact]
    public void Read_only_firmware_refuses_every_change_without_passing_it_on()
    {
        var inner = new RecordingFirmware();
        var firmware = new ReadOnlyFirmware(inner, "not on this laptop");
        Assert.Equal("not on this laptop", Assert.Throws<FirmwareException>(() => firmware.SetProfile(PowerProfile.Turbo)).Message);
        Assert.Throws<FirmwareException>(() => firmware.SetFanMode(FanMode.Max));
        Assert.Throws<FirmwareException>(() => firmware.SetFanMode(FanMode.Auto));
        Assert.Throws<FirmwareException>(() => firmware.SetCustomFanSpeeds(60, 60));
        Assert.Empty(inner.Writes);
    }

    [Fact]
    public void The_fan_rules_never_write_through_read_only_firmware()
    {
        // As the helper runs on an untested laptop: a second of checks after another, hot or not, then stopping.
        var inner = new RecordingFirmware { Temp = 95, Mode = FanMode.Custom };
        var fans = new FanController(new ReadOnlyFirmware(inner, "no"));
        for (int tick = 0; tick < 20; tick++) fans.Tick();
        inner.Temp = 40;
        for (int tick = 0; tick < 20; tick++) fans.Tick();
        fans.Release();
        Assert.Empty(inner.Writes);
        Assert.Equal(40, fans.LastReading?.CpuTemp);
    }

    [Fact]
    public void A_status_from_an_older_helper_reads_as_revision_0_and_not_read_only()
    {
        var old = JsonSerializer.Deserialize<ThermalStatus>("""{"Ok":true,"FanMode":"Auto"}""", ThermalProtocol.Json)!;
        Assert.Equal(0, old.HelperRevision);
        Assert.False(old.ReadOnly);
        Assert.Null(old.Model);
        Assert.True(old.HelperRevision < ThermalProtocol.HelperRevision);
    }

    [Fact]
    public void The_model_and_read_only_state_survive_the_pipe()
    {
        var sent = new ThermalStatus { Ok = true, Model = "Nitro AN515-57", ReadOnly = true, HelperRevision = ThermalProtocol.HelperRevision };
        var received = JsonSerializer.Deserialize<ThermalStatus>(JsonSerializer.Serialize(sent, ThermalProtocol.Json), ThermalProtocol.Json)!;
        Assert.Equal("Nitro AN515-57", received.Model);
        Assert.True(received.ReadOnly);
        Assert.Equal(ThermalProtocol.HelperRevision, received.HelperRevision);
    }
}
