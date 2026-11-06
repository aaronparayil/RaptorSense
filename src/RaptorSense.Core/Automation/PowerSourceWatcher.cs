using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RaptorSense.Automation;

/// <summary>Tells when the laptop moves between battery and mains power, using a Windows notification rather than checking repeatedly.</summary>
[SupportedOSPlatform("windows")]
public sealed class PowerSourceWatcher : IDisposable
{
    static readonly Guid PowerSourceSetting = new("5d3e9a59-e9d5-4b00-a6bd-ff34ff516548");   // GUID_ACDC_POWER_SOURCE
    const uint DEVICE_NOTIFY_CALLBACK = 2, PBT_POWERSETTINGCHANGE = 0x8013;
    const uint OnMains = 0;

    readonly NotifyCallback callback;   // kept in a field so the GC cannot collect the delegate while Windows calls it
    IntPtr registration;

    /// <summary>Raised on a Windows thread with true when on battery. It also fires once just after this object is created.</summary>
    public event Action<bool>? Changed;

    public PowerSourceWatcher()
    {
        callback = OnNotify;
        var parameters = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { Callback = callback };
        var setting = PowerSourceSetting;
        PowerSettingRegisterNotification(ref setting, DEVICE_NOTIFY_CALLBACK, ref parameters, out registration);
    }

    /// <summary>True when running on battery right now. False if Windows can't say.</summary>
    public static bool IsOnBattery => GetSystemPowerStatus(out var status) && status.ACLineStatus == 0;

    uint OnNotify(IntPtr context, uint type, IntPtr setting)
    {
        if (type == PBT_POWERSETTINGCHANGE)
        {
            // POWERBROADCAST_SETTING: a GUID, the data length, then the data (0 = mains, 1 = battery, 2 = UPS).
            uint source = (uint)Marshal.ReadInt32(setting, Marshal.SizeOf<Guid>() + sizeof(uint));
            Changed?.Invoke(source != OnMains);
        }
        return 0;
    }

    public void Dispose()
    {
        if (registration != IntPtr.Zero) PowerSettingUnregisterNotification(registration);
        registration = IntPtr.Zero;
    }

    delegate uint NotifyCallback(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { public NotifyCallback Callback; public IntPtr Context; }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("powrprof.dll")] static extern uint PowerSettingRegisterNotification(ref Guid setting, uint flags, ref DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS recipient, out IntPtr registration);
    [DllImport("powrprof.dll")] static extern uint PowerSettingUnregisterNotification(IntPtr registration);
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
