using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RaptorSense.Hardware;

/// <summary>Reads the battery through Windows' battery driver. No administrator rights or helper needed.</summary>
public static class WindowsBattery
{
    static readonly Guid BatteryClass = new("72631e54-78a4-11d0-bcf7-00aa00b7b32a");   // GUID_DEVCLASS_BATTERY

    /// <summary>The first battery's wear data, or null if there is no battery or it can't be read.</summary>
    public static BatteryInfo? TryRead()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var guid = BatteryClass;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return null;
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            return SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, 0, ref data) && GetInterfacePath(set, ref data) is { } path
                ? Query(path)
                : null;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    static BatteryInfo? Query(string path)
    {
        using var battery = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (battery.IsInvalid) return null;

        uint noWait = 0;
        if (!DeviceIoControl(battery, IOCTL_BATTERY_QUERY_TAG, ref noWait, sizeof(uint), out uint tag, sizeof(uint), out _, IntPtr.Zero) || tag == 0)
            return null;

        var query = new BATTERY_QUERY_INFORMATION { BatteryTag = tag, InformationLevel = BatteryInformation };
        if (!DeviceIoControl(battery, IOCTL_BATTERY_QUERY_INFORMATION, ref query, Marshal.SizeOf<BATTERY_QUERY_INFORMATION>(),
                out BATTERY_INFORMATION info, Marshal.SizeOf<BATTERY_INFORMATION>(), out _, IntPtr.Zero))
            return null;

        return BatteryInfo.FromDriver(info.Capabilities, info.DesignedCapacity, info.FullChargedCapacity, info.CycleCount);
    }

    static string? GetInterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int required, IntPtr.Zero);
        if (required == 0) return null;
        IntPtr detail = Marshal.AllocHGlobal(required);
        try
        {
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);   // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(detail + 4);
        }
        finally { Marshal.FreeHGlobal(detail); }
    }

    const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
    const int BatteryInformation = 0;   // BATTERY_QUERY_INFORMATION_LEVEL
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    const uint IOCTL_BATTERY_QUERY_TAG = 0x294040, IOCTL_BATTERY_QUERY_INFORMATION = 0x294044;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct BATTERY_QUERY_INFORMATION { public uint BatteryTag; public int InformationLevel; public int AtRate; }

    [StructLayout(LayoutKind.Sequential)]
    struct BATTERY_INFORMATION
    {
        public uint Capabilities;
        public byte Technology, Reserved1, Reserved2, Reserved3;
        public uint Chemistry;
        public uint DesignedCapacity, FullChargedCapacity, DefaultAlert1, DefaultAlert2, CriticalBias, CycleCount;
    }

    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref uint input, int inputSize, out uint output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle h, uint code, ref BATTERY_QUERY_INFORMATION input, int inputSize, out BATTERY_INFORMATION output, int outputSize, out int returned, IntPtr overlapped);
}
