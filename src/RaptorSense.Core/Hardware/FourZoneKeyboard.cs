using System.Text.Json.Serialization;
using RaptorSense.Lighting;

namespace RaptorSense.Hardware;

/// <summary>The 4-zone keyboard's own effects, by the Linux module's numbering (docs/protocol.md, "Nitro 5 AN515-57").</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]   // readable in profile files: "Mode": "Wave"
public enum FourZoneMode : byte
{
    Static = 0, Breathing = 1, Neon = 2, Wave = 3, Shifting = 4, Zoom = 5,
}

/// <summary>Which way <see cref="FourZoneMode.Wave"/> and the other moving effects go.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FourZoneDirection : byte
{
    RightToLeft = 1, LeftToRight = 2,
}

/// <summary>The order a firmware keyboard takes its calls in. Laptops differ in which one lights it.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FourZoneOrder
{
    /// <summary>The zones are switched on and coloured, then static mode is set: the AN515-57, and most others.</summary>
    ZonesFirst,

    /// <summary>Static mode is set, then the zones are switched on and coloured: the PHN14-51.</summary>
    StaticFirst,
}

/// <summary>The keyboard of the Nitro 5 AN515-57 (four zones, left to right). Needs administrator rights, so it lives behind the helper.</summary>
public interface IFourZoneKeyboard
{
    /// <summary>
    /// One colour per zone (left to right), brightness 0..100. With <paramref name="confirm"/> the colours are read
    /// back before the call returns; without, it is quicker, for effects that send a new frame several times a second.
    /// </summary>
    /// <param name="order">For keyboards in the firmware; others ignore it.</param>
    void SetZones(IReadOnlyList<Rgb> zones, int brightness, bool confirm = true, FourZoneOrder order = FourZoneOrder.ZonesFirst);

    /// <summary>One of the keyboard's own effects, in one colour where the effect uses one.</summary>
    void SetEffect(FourZoneMode mode, Rgb color, int speed, int brightness, FourZoneDirection direction);
}

/// <summary>
/// Byte layouts for the Nitro 5 AN515-57's 4-zone keyboard, which goes through the same <c>AcerGamingFunction</c> calls
/// as the Helios's light bar. Static zone colours were confirmed on an AN515-57 (BIOS V1.22, 2026-11-16), including the
/// read-back; the effect layout comes from Linux sources and only mode 2 has been sent so far (docs/protocol.md).
/// </summary>
public static class FourZoneProtocol
{
    public const int Zones = 4;
    public const int MaxBrightness = 100;
    public const int MinSpeed = 1, MaxSpeed = 9, DefaultSpeed = 5;

    /// <summary>
    /// <c>SetGamingLED</c> value (a <c>UInt64</c> on the AN515-57) sent before the first change, after a
    /// <c>GetGamingSysInfo(0)</c> read: bit 3 lets the keyboard take updates, bits 40-43 pick all four zones.
    /// </summary>
    public const ulong EnableAllZones = 0x08UL | (0x0FUL << 40);

    /// <summary>
    /// <c>SetGamingRgbKb</c> argument (a <c>UInt32</c>) for one zone (0 = leftmost .. 3): the bytes <c>mask R G B</c>,
    /// so <c>mask | r &lt;&lt; 8 | g &lt;&lt; 16 | b &lt;&lt; 24</c> with masks 1, 2, 4, 8. Unlike
    /// <see cref="LightBarProtocol.ZoneColor"/>, there is no 0x08 or mask in the upper bytes.
    /// </summary>
    public static uint ZoneColor(int zone, Rgb color) => ZoneMask(zone) | ((uint)color.R << 8) | ((uint)color.G << 16) | ((uint)color.B << 24);

    /// <summary>The zone's bit, which is also the <c>GetGamingRgbKb</c> argument that reads it back.</summary>
    public static uint ZoneMask(int zone)
    {
        if (zone is < 0 or >= Zones) throw new ArgumentOutOfRangeException(nameof(zone));
        return 1u << zone;
    }

    /// <summary>
    /// The colour in a <c>GetGamingRgbKb</c> reply: status in byte 0, then R, G, B (e.g. <c>0xFF00AA00</c> is AA00FF).
    /// Null when the status isn't 0.
    /// </summary>
    public static Rgb? ReadZoneColor(ulong output) =>
        (output & 0xFF) != 0 ? null : new Rgb((byte)(output >> 8), (byte)(output >> 16), (byte)(output >> 24));

    /// <summary>The 16-byte <c>SetGamingKBBacklight</c> payload sent after the zone colours, to keep them static.</summary>
    public static byte[] StaticBacklight(int brightness) =>
        [0, 0, ClampBrightness(brightness), 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0];

    /// <summary>
    /// The same for <see cref="FourZoneOrder.StaticFirst"/>, sent before the zone colours. Byte 8 is 3 here, as the
    /// PHN14-51's owner had it (Linuwu-Sense issue 150); the AN515 models need 0 there.
    /// </summary>
    public static byte[] StaticFirstBacklight(int brightness) =>
        [0, 0, ClampBrightness(brightness), 0, 0, 0, 0, 0, 3, 1, 0, 0, 0, 0, 0, 0];

    /// <summary>The 16-byte <c>SetGamingKBBacklight</c> payload that starts one of the keyboard's own effects.</summary>
    public static byte[] EffectBacklight(FourZoneMode mode, Rgb color, int speed, int brightness,
        FourZoneDirection direction = FourZoneDirection.RightToLeft)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (mode is FourZoneMode.Static) return StaticBacklight(brightness);
        return
        [
            (byte)mode, (byte)Math.Clamp(speed, MinSpeed, MaxSpeed), ClampBrightness(brightness),
            mode is FourZoneMode.Wave ? (byte)8 : (byte)0, (byte)direction,
            color.R, color.G, color.B, 0, 1, 0, 0, 0, 0, 0, 0,
        ];
    }

    static byte ClampBrightness(int brightness) => (byte)Math.Clamp(brightness, 0, MaxBrightness);
}
