using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RaptorSense.Hardware;

/// <summary>HID access through hid.dll / SetupAPI. No administrator rights needed for the vendor collections.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHidTransport : IHidTransport
{
    // Acer's lighting service serialises its transfers with this mutex; holding it keeps frames from interleaving with theirs.
    const string AcerMutexFormat = @"Global\ECHIDFeatureVID_{0:X4}&PID_{1:X4}";

    readonly SafeFileHandle handle;
    readonly int featureLength;
    readonly int outputLength;
    readonly ushort vid, pid;

    WindowsHidTransport(SafeFileHandle handle, ushort vid, ushort pid, int featureLength, int outputLength)
    {
        this.handle = handle;
        this.vid = vid;
        this.pid = pid;
        this.featureLength = featureLength;
        this.outputLength = outputLength;
    }

    public string Description => $"{vid:X4}:{pid:X4} (feature {featureLength} B, output {outputLength} B)";

    /// <summary>Opens the first present collection with this vendor id, one of the product ids, and the usage page.</summary>
    public static WindowsHidTransport? Open(ushort vid, IReadOnlyList<ushort> pids, ushort usagePage)
    {
        HidD_GetHidGuid(out Guid hidGuid);
        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data); i++)
            {
                string? path = GetInterfacePath(set, ref data);
                if (path is null) continue;
                foreach (ushort pid in pids)
                {
                    if (!path.Contains($"vid_{vid:x4}&pid_{pid:x4}", StringComparison.OrdinalIgnoreCase)) continue;
                    var transport = TryOpen(path, vid, pid, usagePage);
                    if (transport is not null) return transport;
                }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    static WindowsHidTransport? TryOpen(string path, ushort vid, ushort pid, ushort usagePage)
    {
        var h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) return null;
        if (!HidD_GetPreparsedData(h, out IntPtr preparsed)) { h.Dispose(); return null; }
        HIDP_CAPS caps;
        try { HidP_GetCaps(preparsed, out caps); }
        finally { HidD_FreePreparsedData(preparsed); }
        if (caps.UsagePage != usagePage) { h.Dispose(); return null; }
        return new WindowsHidTransport(h, vid, pid, caps.FeatureReportByteLength, caps.OutputReportByteLength);
    }

    public void SetFeature(ReadOnlySpan<byte> payload)
    {
        var buf = WithReportId(payload, featureLength);
        if (!HidD_SetFeature(handle, buf, buf.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_SetFeature failed");
    }

    public byte[] GetFeature()
    {
        var buf = new byte[featureLength];
        if (!HidD_GetFeature(handle, buf, buf.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_GetFeature failed");
        return buf[1..];
    }

    // WriteFile is synchronous here: it returns when the transfer is done, so frames need no pacing (docs/protocol.md).
    public void WriteOutput(ReadOnlySpan<byte> payload)
    {
        var buf = WithReportId(payload, outputLength);
        if (!WriteFile(handle, buf, buf.Length, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteFile failed");
    }

    public IDisposable Exclusive()
    {
        Mutex? mutex = null;
        try
        {
            if (Mutex.TryOpenExisting(string.Format(AcerMutexFormat, vid, pid), out mutex))
            {
                try { if (mutex.WaitOne(1000)) return new Releaser(mutex); }
                catch (AbandonedMutexException) { return new Releaser(mutex); }
            }
        }
        catch (UnauthorizedAccessException) { }
        mutex?.Dispose();
        return Releaser.None;
    }

    public void Dispose() => handle.Dispose();

    static byte[] WithReportId(ReadOnlySpan<byte> payload, int length)
    {
        var buf = new byte[length];   // byte 0 = report id 0
        payload[..Math.Min(payload.Length, length - 1)].CopyTo(buf.AsSpan(1));
        return buf;
    }

    sealed class Releaser(Mutex? mutex) : IDisposable
    {
        public static readonly Releaser None = new(null);

        public void Dispose()
        {
            if (mutex is null) return;
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
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
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr preparsed);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr preparsed);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, byte[] buf, int len, out int written, IntPtr overlapped);
}
