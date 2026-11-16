using RaptorSense.Hardware;
using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

/// <summary>The Linux pieces that read and write plain files, tried against folders made to look like the real ones.</summary>
public sealed class LinuxSystemTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "raptorsense-linux-" + Guid.NewGuid().ToString("N"));

    public LinuxSystemTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    string Supply(string name, params (string File, string Value)[] files)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        foreach (var (file, value) in files) File.WriteAllText(Path.Combine(dir, file), value + "\n");
        return dir;
    }

    [Fact]
    public void A_battery_that_reports_energy_is_read_in_milliwatt_hours()
    {
        Supply("AC", ("type", "Mains"), ("online", "1"));
        Supply("BAT1", ("type", "Battery"), ("energy_full_design", "90610000"), ("energy_full", "73100000"), ("cycle_count", "512"));

        var battery = LinuxBattery.TryRead(root);
        Assert.Equal(new BatteryInfo(512, 90610, 73100), battery);
        Assert.Equal(81, battery!.Value.HealthPercent);
    }

    [Fact]
    public void A_battery_that_reports_charge_is_converted_with_its_design_voltage()
    {
        // 5000 mAh design and 4000 mAh now at 15.4 V: 77 Wh and 61.6 Wh.
        Supply("BAT0", ("type", "Battery"), ("charge_full_design", "5000000"), ("charge_full", "4000000"), ("voltage_min_design", "15400000"), ("cycle_count", "0"));

        var battery = LinuxBattery.TryRead(root)!.Value;
        Assert.Equal(77000, battery.DesignMilliwattHours);
        Assert.Equal(61600, battery.FullChargeMilliwattHours);
        Assert.Null(battery.CycleCount);   // the kernel writes 0 for "not told"
        Assert.Equal(80, battery.HealthPercent);
    }

    [Fact]
    public void A_mouse_battery_is_not_the_laptops_battery()
    {
        Supply("hidpp_battery_0", ("type", "Battery"), ("scope", "Device"), ("energy_full", "1000000"), ("energy_full_design", "1000000"));
        Assert.Null(LinuxBattery.TryRead(root));

        Supply("BAT1", ("type", "Battery"), ("energy_full_design", "50000000"), ("energy_full", "45000000"));
        Assert.Equal(90, LinuxBattery.TryRead(root)!.Value.HealthPercent);
    }

    [Fact]
    public void No_power_supply_folder_means_no_battery()
    {
        Assert.Null(LinuxBattery.TryRead(Path.Combine(root, "missing")));
        Assert.Null(LinuxBattery.TryRead(root));
    }

    [Fact]
    public void The_model_is_read_from_the_dmi_product_name()
    {
        File.WriteAllText(Path.Combine(root, "product_name"), "Predator PH16-71\n");
        Assert.Equal("Predator PH16-71", LaptopModels.ReadModelFromDmi(root));
        Assert.True(LaptopModels.IsVerified(LaptopModels.ReadModelFromDmi(root)));

        File.WriteAllText(Path.Combine(root, "product_name"), "  \n");
        Assert.Null(LaptopModels.ReadModelFromDmi(root));
        Assert.Null(LaptopModels.ReadModelFromDmi(Path.Combine(root, "missing")));
    }

    [Fact]
    public void Autostart_writes_a_desktop_file_and_removes_it_again()
    {
        var folder = Path.Combine(root, "autostart");
        const string exe = "/home/someone/RaptorSense/RaptorSense";
        Assert.False(DesktopAutostart.IsEnabled(folder, exe));

        DesktopAutostart.Enable(folder, exe, "--minimized");
        var lines = File.ReadAllLines(Path.Combine(folder, "raptorsense.desktop"));
        Assert.Equal("[Desktop Entry]", lines[0]);
        Assert.Contains("Type=Application", lines);
        Assert.Contains("Exec=\"/home/someone/RaptorSense/RaptorSense\" --minimized", lines);
        Assert.True(DesktopAutostart.IsEnabled(folder, exe));
        Assert.False(DesktopAutostart.IsEnabled(folder, "/somewhere/else/RaptorSense"));   // a copy that has since moved
        Assert.False(DesktopAutostart.IsEnabled(folder, "/home/someone/RaptorSense/Raptor"));

        DesktopAutostart.Disable(folder);
        Assert.False(DesktopAutostart.IsEnabled(folder, exe));
        DesktopAutostart.Disable(folder);   // already off: nothing to do
    }

    [Fact]
    public void A_path_with_awkward_characters_is_escaped_in_the_desktop_file()
    {
        var folder = Path.Combine(root, "autostart");
        const string exe = "/home/a b/50% \"new\" $HOME/RaptorSense";
        DesktopAutostart.Enable(folder, exe, "--minimized");
        Assert.Contains("Exec=\"/home/a b/50%% \\\"new\\\" \\$HOME/RaptorSense\" --minimized", File.ReadAllLines(Path.Combine(folder, "raptorsense.desktop")));
        Assert.True(DesktopAutostart.IsEnabled(folder, exe));
    }
}
