using System.Text.Json.Serialization;
using RaptorSense.Lighting;

namespace RaptorSense.Hardware;

/// <summary>The 4-zone keyboard's own effects, by the Linux module's numbering (docs/protocol.md, "Nitro 5 AN515-57").</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FourZoneMode : byte
{
    Static = 0, Breathing = 1, Neon = 2, Wave = 3, Shifting = 4, Zoom = 5,
}

/// <summary>Which way <see cref="FourZoneMode.Wave"/> and the other moving effects go.</summary>
public enum FourZoneDirection : byte
{
    RightToLeft = 1, LeftToRight = 2,
}

/// <summary>
/// Byte layouts for the Nitro 5 AN515-57's 4-zone keyboard, which goes through the same <c>AcerGamingFunction</c> calls
/// as the Helios's light bar. <b>A hypothesis, not checked on the laptop:</b> the layouts come from Linux sources
/// (docs/protocol.md). Nothing sends them yet; that waits until the read-only checks on the laptop have been done.
/// </summary>
public static class FourZoneProtocol
{
    public const int Zones = 4;
    public const int MaxBrightness = 100;
    public const int MinSpeed = 1, MaxSpeed = 9, DefaultSpeed = 5;

    /// <summary>
    /// <c>SetGamingLED</c> value sent once at start, after a <c>GetGamingSysInfo(0)</c> read: bit 3 lets the keyboard
    /// take updates, bits 40-43 pick all four zones. On Windows the method may want a byte array instead; check first.
    /// </summary>
    public const ulong EnableAllZones = 0x08UL | (0x0FUL << 40);

    /// <summary>
    /// <c>SetGamingRgbKb</c> argument for one zone (0 = leftmost .. 3): the bytes <c>mask R G B</c>, so
    /// <c>mask | r &lt;&lt; 8 | g &lt;&lt; 16 | b &lt;&lt; 24</c> with masks 1, 2, 4, 8. Unlike
    /// <see cref="LightBarProtocol.ZoneColor"/>, there is no 0x08 or mask in the upper bytes.
    /// </summary>
    public static ulong ZoneColor(int zone, Rgb color)
    {
        if (zone is < 0 or >= Zones) throw new ArgumentOutOfRangeException(nameof(zone));
        return (1UL << zone) | ((ulong)color.R << 8) | ((ulong)color.G << 16) | ((ulong)color.B << 24);
    }

    /// <summary>The 16-byte <c>SetGamingKBBacklight</c> payload sent after the zone colours, to keep them static.</summary>
    public static byte[] StaticBacklight(int brightness) =>
        [0, 0, ClampBrightness(brightness), 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0];

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
