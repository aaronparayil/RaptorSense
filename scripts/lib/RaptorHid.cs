// Minimal Win32 HID access to the PH16-71 keyboard lighting interface (Chicony 04F2:0117, usage page 0xFF02).
// Protocol: docs/protocol.md. Written for the C# 5 compiler that Windows PowerShell 5.1's Add-Type uses.
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace RaptorSense
{
    public sealed class KeyboardHid : IDisposable
    {
        // Acer's lighting service serialises its transfers with this mutex; holding it keeps our frames from interleaving with theirs.
        const string AcerMutexFormat = @"Global\ECHIDFeatureVID_{0:X4}&PID_{1:X4}";
        static readonly ushort[] KnownPids = { 0x0117, 0x011A, 0x0119 };
        const ushort Vid = 0x04F2;
        const ushort LightingUsagePage = 0xFF02;

        public string Path { get; private set; }
        public ushort Pid { get; private set; }
        public int FeatureLength { get; private set; }
        public int OutputLength { get; private set; }
        public bool HoldsAcerMutex { get { return acerMutex != null; } }

        SafeFileHandle handle;
        Mutex acerMutex;

        KeyboardHid() { }

        public static KeyboardHid Open()
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var data = new SP_DEVICE_INTERFACE_DATA();
                data.cbSize = Marshal.SizeOf(data);
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data); i++)
                {
                    string path = GetInterfacePath(set, ref data);
                    if (path == null) continue;
                    string lower = path.ToLowerInvariant();
                    foreach (ushort pid in KnownPids)
                    {
                        if (!lower.Contains(string.Format("vid_{0:x4}&pid_{1:x4}", Vid, pid))) continue;
                        KeyboardHid kb = TryOpen(path, pid);
                        if (kb != null) return kb;
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            throw new InvalidOperationException("Keyboard lighting interface (04F2, usage page FF02) not found.");
        }

        static KeyboardHid TryOpen(string path, ushort pid)
        {
            SafeFileHandle h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) return null;
            IntPtr preparsed;
            if (!HidD_GetPreparsedData(h, out preparsed)) { h.Dispose(); return null; }
            HIDP_CAPS caps;
            try { HidP_GetCaps(preparsed, out caps); }
            finally { HidD_FreePreparsedData(preparsed); }
            if (caps.UsagePage != LightingUsagePage) { h.Dispose(); return null; }

            var kb = new KeyboardHid();
            kb.handle = h;
            kb.Path = path;
            kb.Pid = pid;
            kb.FeatureLength = caps.FeatureReportByteLength;
            kb.OutputLength = caps.OutputReportByteLength;
            try
            {
                Mutex m;
                if (Mutex.TryOpenExisting(string.Format(AcerMutexFormat, Vid, pid), out m)) kb.acerMutex = m;
            }
            catch (UnauthorizedAccessException) { }
            return kb;
        }

        // 8-byte command packet: 7 bytes plus checksum 0xFF - sum.
        public static byte[] Packet(params byte[] seven)
        {
            var p = new byte[8];
            int sum = 0;
            for (int i = 0; i < 7 && i < seven.Length; i++) { p[i] = seven[i]; sum += seven[i]; }
            p[7] = (byte)((0xFF - sum) & 0xFF);
            return p;
        }

        public void SetFeature(byte[] payload)
        {
            var buf = new byte[FeatureLength];   // byte 0 = report id 0
            Array.Copy(payload, 0, buf, 1, Math.Min(payload.Length, FeatureLength - 1));
            if (!HidD_SetFeature(handle, buf, buf.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_SetFeature");
        }

        public byte[] GetFeature()
        {
            var buf = new byte[FeatureLength];
            if (!HidD_GetFeature(handle, buf, buf.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_GetFeature");
            var data = new byte[FeatureLength - 1];
            Array.Copy(buf, 1, data, 0, data.Length);
            return data;
        }

        public void WriteOutput(byte[] payload)
        {
            var buf = new byte[OutputLength];
            Array.Copy(payload, 0, buf, 1, Math.Min(payload.Length, OutputLength - 1));
            int written;
            if (!WriteFile(handle, buf, buf.Length, out written, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "WriteFile");
        }

        public int GetBrightness()
        {
            return Locked(() => { SetFeature(new byte[] { 0x88, 0, 0, 0, 0, 0, 0, 0x77 }); return GetFeature()[4]; });
        }

        // cells: 128 x (R, G, B) = 384 bytes, cell = column * 6 + row. Returns the time the upload took.
        public TimeSpan SendFrame(byte[] rgb, int brightness, int pacingMs)
        {
            if (rgb.Length != 128 * 3) throw new ArgumentException("rgb must hold 128 cells x 3 bytes");
            var data = new byte[512];
            for (int c = 0; c < 128; c++)
            {
                data[c * 4 + 1] = rgb[c * 3];
                data[c * 4 + 2] = rgb[c * 3 + 1];
                data[c * 4 + 3] = rgb[c * 3 + 2];
            }
            var watch = Stopwatch.StartNew();
            Locked(() =>
            {
                SetFeature(new byte[] { 0x88, 0, 0, 0, 0, 0, 0, 0x77 });
                GetFeature();
                SetFeature(new byte[] { 0x12, 0, 0, 0x08, 0, 0, 0, 0xE5 });
                var chunk = new byte[64];
                for (int i = 0; i < 8; i++)
                {
                    Array.Copy(data, i * 64, chunk, 0, 64);
                    WriteOutput(chunk);
                    if (pacingMs > 0) Thread.Sleep(pacingMs);
                }
                SetFeature(Packet(0x08, 0x02, 0x33, 0x05, (byte)brightness, 0x08, 0x01));
                return 0;
            });
            return watch.Elapsed;
        }

        // Built-in firmware effect, e.g. 3 = wave. speedByte: 0 fastest .. 11 slowest.
        public void SetEffect(byte effect, byte r, byte g, byte b, byte speedByte, byte brightness, byte direction)
        {
            Locked(() =>
            {
                SetFeature(new byte[] { 0xB1, 0, 0, 0, 0, 0, 0, 0x4E });
                SetFeature(Packet(0x14, 0, 0, r, g, b, 0));
                SetFeature(Packet(0x08, 0x02, effect, speedByte, brightness, 0x01, (byte)(direction + 1)));
                return 0;
            });
        }

        T Locked<T>(Func<T> body)
        {
            bool held = false;
            if (acerMutex != null)
            {
                try { held = acerMutex.WaitOne(1000); }
                catch (AbandonedMutexException) { held = true; }
            }
            try { return body(); }
            finally { if (held) acerMutex.ReleaseMutex(); }
        }

        // Thread.Sleep follows the system timer tick (about 15.6 ms by default); 1 ms resolution makes short pacing real.
        public static void SetPreciseTimer(bool on)
        {
            if (on) timeBeginPeriod(1); else timeEndPeriod(1);
        }

        public void Dispose()
        {
            if (acerMutex != null) { acerMutex.Dispose(); acerMutex = null; }
            if (handle != null) { handle.Dispose(); handle = null; }
        }

        static string GetInterfacePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
        {
            int required;
            SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required == 0) return null;
            IntPtr detail = Marshal.AllocHGlobal(required);
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);   // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
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
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(SafeFileHandle h, byte[] buf, int len, out int written, IntPtr overlapped);
    }
}
