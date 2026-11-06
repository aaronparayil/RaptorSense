using RaptorSense.Hardware;
using Xunit;

namespace RaptorSense.Tests;

public sealed class BatteryInfoTests
{
    const uint SystemBattery = 0x80000000, RelativeCapacity = 0x40000000, Unknown = 0xFFFFFFFF;

    // Values read from the PH16-71 on 2026-11-06.
    [Fact]
    public void Reads_real_values()
    {
        var battery = BatteryInfo.FromDriver(SystemBattery, 90614, 73135, 505);

        Assert.Equal(505, battery.CycleCount);
        Assert.Equal(90.614, battery.DesignWattHours);
        Assert.Equal(73.135, battery.FullChargeWattHours);
        Assert.Equal(81, battery.HealthPercent);
    }

    [Fact]
    public void Zero_cycles_means_not_reported()
    {
        Assert.Null(BatteryInfo.FromDriver(SystemBattery, 90614, 73135, 0).CycleCount);
    }

    [Theory]
    [InlineData(0u, 73135u)]
    [InlineData(Unknown, 73135u)]
    [InlineData(90614u, 0u)]
    [InlineData(90614u, Unknown)]
    public void No_health_without_both_capacities(uint design, uint fullCharge)
    {
        Assert.Null(BatteryInfo.FromDriver(SystemBattery, design, fullCharge, 505).HealthPercent);
    }

    [Fact]
    public void Relative_capacities_are_not_shown_as_watt_hours()
    {
        var battery = BatteryInfo.FromDriver(SystemBattery | RelativeCapacity, 100, 80, 505);

        Assert.Null(battery.DesignWattHours);
        Assert.Null(battery.FullChargeWattHours);
        Assert.Null(battery.HealthPercent);
        Assert.Equal(505, battery.CycleCount);   // cycles don't depend on the capacity units
    }

    [Fact]
    public void Health_is_capped_at_100_percent()
    {
        Assert.Equal(100, BatteryInfo.FromDriver(SystemBattery, 90000, 92000, 3).HealthPercent);
    }
}
