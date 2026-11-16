using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RaptorSense.Hardware;

/// <summary>A HID interface as Linux lists it under <c>/sys/class/hidraw</c>.</summary>
public sealed record HidrawDevice(string Name, ushort VendorId, ushort ProductId, HidReportDescriptor Descriptor)
{
    public string DevicePath => $"/dev/{Name}";

    /// <summary>
    /// The interfaces with this vendor id, one of the product ids, and a collection on this usage page.
    /// <paramref name="sysRoot"/> is <c>/sys/class/hidraw</c>; reading it needs no special rights.
    /// </summary>
    public static IReadOnlyList<HidrawDevice> Find(string sysRoot, ushort vid, IReadOnlyList<ushort> pids, ushort usagePage)
    {
        var found = new List<HidrawDevice>();
        if (!Directory.Exists(sysRoot)) return found;
        foreach (var dir in Directory.EnumerateDirectories(sysRoot, "hidraw*").OrderBy(d => d, StringComparer.Ordinal))
        {
            try
            {
                if (!TryReadIds(Path.Combine(dir, "device", "uevent"), out ushort deviceVid, out ushort devicePid)) continue;
                if (deviceVid != vid || !pids.Contains(devicePid)) continue;
                var descriptor = HidReportDescriptor.Parse(File.ReadAllBytes(Path.Combine(dir, "device", "report_descriptor")));
                if (descriptor.Has(usagePage)) found.Add(new HidrawDevice(Path.GetFileName(dir), deviceVid, devicePid, descriptor));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // unplugged while we looked
        }
        return found;
    }

    /// <summary>
    /// The ioctl number for a feature report of this length, id byte included: <c>HIDIOCSFEATURE</c> (number 6) and
    /// <c>HIDIOCGFEATURE</c> (number 7) of linux/hidraw.h, which are _IOC(read | write, 'H', number, length).
    /// </summary>
    public static uint FeatureRequest(int number, int length) => (3u << 30) | ((uint)length << 16) | ((uint)'H' << 8) | (uint)number;

    // The line reads HID_ID=0003:000004F2:00000117 (bus, vendor, product, in hex).
    static bool TryReadIds(string ueventPath, out ushort vid, out ushort pid)
    {
        vid = pid = 0;
        if (!File.Exists(ueventPath)) return false;
        foreach (var line in File.ReadLines(ueventPath))
        {
            if (!line.StartsWith("HID_ID=", StringComparison.Ordinal)) continue;
            var parts = line["HID_ID=".Length..].Split(':');
            return parts.Length == 3
                && uint.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)
                && uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint p)
                && (vid = (ushort)v) == v && (pid = (ushort)p) == p;
        }
        return false;
    }
}

/// <summary>
/// HID access through Linux's hidraw devices. Linux gives one device per USB interface, so the lighting interface is
/// found by the usage page in its report descriptor. <b>Not yet tried on the real keyboard.</b>
/// <para>
/// A normal user may not open <c>/dev/hidraw*</c> until a udev rule allows it: see <c>linux/70-raptorsense.rules</c>.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHidTransport : IHidTransport
{
    public const string SysRoot = "/sys/class/hidraw";

    /// <summary>
    /// Milliseconds to wait after each output report. TrueSense needed about 16 on Linux or the keyboard dropped a
    /// frame's last packet; on Windows none is needed. Unknown for this code until it has run on the keyboard, so it
    /// can be set from outside while that is found out.
    /// </summary>
    public const string GapVariable = "RAPTORSENSE_HID_GAP_MS";

    readonly SafeFileHandle handle;
    readonly HidrawDevice device;
    readonly int gapMs;

    LinuxHidTransport(SafeFileHandle handle, HidrawDevice device)
    {
        this.handle = handle;
        this.device = device;
        gapMs = int.TryParse(Environment.GetEnvironmentVariable(GapVariable), out int gap) ? Math.Clamp(gap, 0, 100) : 0;
    }

    public string Description =>
        $"{device.VendorId:X4}:{device.ProductId:X4} on {device.DevicePath} (feature {device.Descriptor.FeatureBytes} B, output {device.Descriptor.OutputBytes} B)";

    /// <summary>
    /// Opens the first present interface with this vendor id, one of the product ids, and the usage page. Null when
    /// there is none; throws <see cref="UnauthorizedAccessException"/> when it is there but may not be opened.
    /// </summary>
    public static LinuxHidTransport? Open(ushort vid, IReadOnlyList<ushort> pids, ushort usagePage)
    {
        foreach (var device in HidrawDevice.Find(SysRoot, vid, pids, usagePage))
        {
            int fd = open(device.DevicePath, O_RDWR | O_CLOEXEC);
            if (fd >= 0) return new LinuxHidTransport(new SafeFileHandle(fd, ownsHandle: true), device);
            int error = Marshal.GetLastPInvokeError();
            if (error is EACCES or EPERM)
                throw new UnauthorizedAccessException(
                    $"the keyboard is there ({device.DevicePath}), but this user may not open it. Install RaptorSense's udev rule (see README: Linux).");
        }
        return null;
    }

    int featureBytes;   // from the descriptor, or the longest sent if the descriptor didn't say

    public void SetFeature(ReadOnlySpan<byte> payload)
    {
        var buf = WithReportId(payload, device.Descriptor.FeatureBytes);
        featureBytes = Math.Max(featureBytes, buf.Length - 1);
        if (ioctl(handle, HidrawDevice.FeatureRequest(6, buf.Length), buf) < 0) throw Failed("HIDIOCSFEATURE");
    }

    public byte[] GetFeature()
    {
        var buf = new byte[Math.Max(featureBytes, device.Descriptor.FeatureBytes) + 1];   // byte 0 = report id 0
        if (ioctl(handle, HidrawDevice.FeatureRequest(7, buf.Length), buf) < 0) throw Failed("HIDIOCGFEATURE");
        return buf[1..];
    }

    public void WriteOutput(ReadOnlySpan<byte> payload)
    {
        var buf = WithReportId(payload, device.Descriptor.OutputBytes);
        if (write(handle, buf, buf.Length) < 0) throw Failed("write");
        if (gapMs > 0) Thread.Sleep(gapMs);
    }

    /// <summary>Nothing else is known to drive the keyboard on Linux, so there is nothing to wait for.</summary>
    public IDisposable Exclusive() => Nothing.Instance;

    public void Dispose() => handle.Dispose();

    static byte[] WithReportId(ReadOnlySpan<byte> payload, int payloadBytes)
    {
        if (payloadBytes == 0) payloadBytes = payload.Length;   // a descriptor that couldn't be read
        var buf = new byte[payloadBytes + 1];   // byte 0 = report id 0
        payload[..Math.Min(payload.Length, payloadBytes)].CopyTo(buf.AsSpan(1));
        return buf;
    }

    static IOException Failed(string call) => new($"{call} failed with error {Marshal.GetLastPInvokeError()}.");

    sealed class Nothing : IDisposable
    {
        public static readonly Nothing Instance = new();
        public void Dispose() { }
    }

    const int O_RDWR = 0x2, O_CLOEXEC = 0x80000, EPERM = 1, EACCES = 13;

    [DllImport("libc", SetLastError = true)] static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);
    [DllImport("libc", SetLastError = true)] static extern int ioctl(SafeFileHandle fd, nuint request, byte[] data);
    [DllImport("libc", SetLastError = true)] static extern nint write(SafeFileHandle fd, byte[] data, nint count);
}
