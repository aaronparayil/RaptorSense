using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RaptorSense.Automation;

/// <summary>Tells when one of Windows' power settings changes, using a Windows notification rather than checking repeatedly.</summary>
[SupportedOSPlatform("windows")]
public abstract class PowerSettingWatcher : IDisposable
{
    const uint DEVICE_NOTIFY_CALLBACK = 2, PBT_POWERSETTINGCHANGE = 0x8013;

    readonly NotifyCallback callback;   // kept in a field so the GC cannot collect the delegate while Windows calls it
    IntPtr registration;

    /// <summary>
    /// Called on a Windows thread with the setting's new value. Windows also calls it once with the current value
    /// a moment after this object is created (seen on a PH16-71: after the constructor has returned).
    /// </summary>
    protected abstract void OnValue(uint value);

    protected PowerSettingWatcher(Guid setting)
    {
        callback = OnNotify;
        var parameters = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { Callback = callback };
        PowerSettingRegisterNotification(ref setting, DEVICE_NOTIFY_CALLBACK, ref parameters, out registration);
    }

    uint OnNotify(IntPtr context, uint type, IntPtr setting)
    {
        // POWERBROADCAST_SETTING: a GUID, the data length, then the data.
        if (type == PBT_POWERSETTINGCHANGE) OnValue((uint)Marshal.ReadInt32(setting, Marshal.SizeOf<Guid>() + sizeof(uint)));
        return 0;
    }

    public void Dispose()
    {
        if (registration != IntPtr.Zero) PowerSettingUnregisterNotification(registration);
        registration = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    delegate uint NotifyCallback(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { public NotifyCallback Callback; public IntPtr Context; }

    [DllImport("powrprof.dll")] static extern uint PowerSettingRegisterNotification(ref Guid setting, uint flags, ref DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS recipient, out IntPtr registration);
    [DllImport("powrprof.dll")] static extern uint PowerSettingUnregisterNotification(IntPtr registration);
}

/// <summary>Tells when the laptop moves between battery and mains power.</summary>
[SupportedOSPlatform("windows")]
public sealed class PowerSourceWatcher : PowerSettingWatcher
{
    const uint OnMains = 0;   // 1 = battery, 2 = UPS

    /// <summary>Raised on a Windows thread with true when on battery.</summary>
    public event Action<bool>? Changed;

    public PowerSourceWatcher() : base(new Guid("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548")) { }   // GUID_ACDC_POWER_SOURCE

    protected override void OnValue(uint source) => Changed?.Invoke(source != OnMains);

    /// <summary>True when running on battery right now. False if Windows can't say.</summary>
    public static bool IsOnBattery => GetSystemPowerStatus(out var status) && status.ACLineStatus == 0;

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}

/// <summary>Tells when Windows turns the screen off and on again. A dimmed screen counts as on.</summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenWatcher : PowerSettingWatcher
{
    const uint Off = 0;   // 1 = on, 2 = dimmed

    /// <summary>Raised on a Windows thread with true when the screen is off.</summary>
    public event Action<bool>? Changed;

    public ScreenWatcher() : base(new Guid("6fe69556-704a-47a0-8f24-c28d936fda47")) { }   // GUID_CONSOLE_DISPLAY_STATE

    protected override void OnValue(uint state) => Changed?.Invoke(state == Off);
}
