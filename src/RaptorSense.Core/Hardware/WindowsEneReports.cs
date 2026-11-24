using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RaptorSense.Hardware;

/// <summary>
/// The ENE controller's feature reports through hid.dll. It sits on I2C, so its device path names ENEK5130 instead of
/// USB ids; the ids are asked for. No administrator rights needed.
/// </summary>
[SupportedOSPlatform("windows")]
sealed class WindowsEneReports : IEneReports
{
    readonly SafeFileHandle handle;
    readonly int featureLength;

    WindowsEneReports(SafeFileHandle handle, int featureLength)
    {
        this.handle = handle;
        this.featureLength = featureLength;
    }

    /// <summary>Opens the controller's collection that has feature reports, or null if there is none.</summary>
    public static WindowsEneReports? Open()
    {
        foreach (var path in WindowsHidTransport.InterfacePaths())
        {
            // Only devices that name the chip or its ids are opened at all.
            if (!path.Contains("ENEK5130", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"vid_{EneProtocol.VendorId:x4}&pid_{EneProtocol.ProductId:x4}", StringComparison.OrdinalIgnoreCase)) continue;

            var handle = WindowsHidTransport.OpenPath(path);
            int length = !handle.IsInvalid && IsController(handle) ? WindowsHidTransport.FeatureLength(handle) : 0;
            if (length >= EneProtocol.LightingLength) return new WindowsEneReports(handle, length);
            handle.Dispose();
        }
        return null;
    }

    static bool IsController(SafeFileHandle handle)
    {
        var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        return HidD_GetAttributes(handle, ref attributes) && attributes is { VendorID: EneProtocol.VendorId, ProductID: EneProtocol.ProductId };
    }

    // Windows wants room for the collection's longest feature report, whichever one is sent.
    public void Set(ReadOnlySpan<byte> report)
    {
        var buffer = new byte[Math.Max(featureLength, report.Length)];
        report.CopyTo(buffer);
        if (!HidD_SetFeature(handle, buffer, buffer.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_SetFeature failed");
    }

    public byte[] Get(byte reportId, int length)
    {
        var buffer = new byte[Math.Max(featureLength, length)];
        buffer[0] = reportId;
        if (!HidD_GetFeature(handle, buffer, buffer.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_GetFeature failed");
        return buffer[..length];
    }

    public void Dispose() => handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID, ProductID, VersionNumber; }

    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES attributes);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
}
