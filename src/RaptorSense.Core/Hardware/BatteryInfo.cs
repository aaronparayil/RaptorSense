namespace RaptorSense.Hardware;

/// <summary>Battery wear as the battery itself reports it. A value is null when this battery doesn't report it.</summary>
public readonly record struct BatteryInfo(int? CycleCount, int? DesignMilliwattHours, int? FullChargeMilliwattHours)
{
    const uint CapacityIsRelative = 0x40000000;   // BATTERY_CAPACITY_RELATIVE: capacities aren't in mWh

    public double? DesignWattHours => DesignMilliwattHours / 1000.0;

    public double? FullChargeWattHours => FullChargeMilliwattHours / 1000.0;

    /// <summary>What a full charge holds now, as a share of what it held when new. Capped at 100: new batteries can test above their rating.</summary>
    public int? HealthPercent => (DesignMilliwattHours, FullChargeMilliwattHours) is (int design, int full)
        ? Math.Min(100, (int)Math.Round(100.0 * full / design))
        : null;

    /// <summary>Builds this from Windows' BATTERY_INFORMATION, where 0 and 0xFFFFFFFF mean "not known".</summary>
    public static BatteryInfo FromDriver(uint capabilities, uint design, uint fullCharge, uint cycles)
    {
        bool relative = (capabilities & CapacityIsRelative) != 0;
        return new BatteryInfo(Known(cycles), relative ? null : Known(design), relative ? null : Known(fullCharge));
    }

    static int? Known(uint value) => value is 0 or > int.MaxValue ? null : (int)value;
}
