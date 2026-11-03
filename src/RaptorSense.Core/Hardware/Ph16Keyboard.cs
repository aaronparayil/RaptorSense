using RaptorSense.Lighting;

namespace RaptorSense.Hardware;

/// <summary>Effects the keyboard runs by itself, with their protocol ids.</summary>
public enum BuiltInEffect : byte
{
    Static = 1, Breathing = 2, Wave = 3, Snake = 5, Ripple = 6, Neon = 8, Raindrop = 10,
    Lightning = 18, Spot = 37, Star = 38, Fireball = 39, Snow = 40, Heartbeat = 41,
}

/// <summary>
/// Per-key lighting protocol of the Chicony 04F2:0117 keyboard (PH16-71 generation), on usage page 0xFF02.
/// Packet formats are in docs/protocol.md.
/// </summary>
public sealed class Ph16Keyboard(IHidTransport transport) : IDisposable
{
    public const ushort VendorId = 0x04F2;
    public static readonly IReadOnlyList<ushort> ProductIds = [0x0117, 0x011A, 0x0119];
    public const ushort LightingUsagePage = 0xFF02;
    public const int MaxBrightness = 50;
    public const int MinSpeed = 1, MaxSpeed = 12;

    static readonly byte[] Query = [0x88, 0, 0, 0, 0, 0, 0, 0x77];
    static readonly byte[] BeginFrame = [0x12, 0, 0, 0x08, 0, 0, 0, 0xE5];
    static readonly byte[] BeginEffect = [0xB1, 0, 0, 0, 0, 0, 0, 0x4E];

    readonly byte[] frameData = new byte[Frame.CellCount * 4];

    public string Description => transport.Description;

    /// <summary>Opens the keyboard, or returns null when it is not present.</summary>
    public static Ph16Keyboard? TryOpen()
    {
        if (OperatingSystem.IsWindows())
        {
            var transport = WindowsHidTransport.Open(VendorId, ProductIds, LightingUsagePage);
            return transport is null ? null : new Ph16Keyboard(transport);
        }
        throw new PlatformNotSupportedException("Keyboard access is only implemented for Windows so far.");
    }

    public int GetBrightness()
    {
        using var _ = transport.Exclusive();
        transport.SetFeature(Query);
        return transport.GetFeature()[4];
    }

    public void SendFrame(Frame frame, int brightness)
    {
        brightness = Math.Clamp(brightness, 0, MaxBrightness);
        var cells = frame.Cells;
        for (int c = 0; c < Frame.CellCount; c++)
        {
            frameData[c * 4 + 1] = cells[c].R;
            frameData[c * 4 + 2] = cells[c].G;
            frameData[c * 4 + 3] = cells[c].B;
        }

        using var _ = transport.Exclusive();
        transport.SetFeature(Query);
        transport.GetFeature();
        transport.SetFeature(BeginFrame);
        for (int i = 0; i < 8; i++) transport.WriteOutput(frameData.AsSpan(i * 64, 64));
        transport.SetFeature(Packet(0x08, 0x02, 0x33, 0x05, (byte)brightness, 0x08, 0x01));
    }

    /// <param name="speed">1 slowest .. 12 fastest.</param>
    /// <param name="direction">0 or 1; only some effects use it.</param>
    public void SetBuiltInEffect(BuiltInEffect effect, Rgb color, int speed, int brightness, int direction = 0)
    {
        speed = Math.Clamp(speed, MinSpeed, MaxSpeed);
        brightness = Math.Clamp(brightness, 0, MaxBrightness);
        using var _ = transport.Exclusive();
        transport.SetFeature(BeginEffect);
        transport.SetFeature(Packet(0x14, 0, 0, color.R, color.G, color.B, 0));
        transport.SetFeature(Packet(0x08, 0x02, (byte)effect, (byte)(MaxSpeed - speed), (byte)brightness, 0x01, (byte)(direction + 1)));
    }

    /// <summary>An 8-byte command: seven bytes plus the checksum 0xFF - sum.</summary>
    public static byte[] Packet(params byte[] seven)
    {
        var p = new byte[8];
        int sum = 0;
        for (int i = 0; i < 7 && i < seven.Length; i++) { p[i] = seven[i]; sum += seven[i]; }
        p[7] = (byte)((0xFF - sum) & 0xFF);
        return p;
    }

    public void Dispose() => transport.Dispose();
}
