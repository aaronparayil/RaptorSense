using System.Text.Json.Serialization;
using RaptorSense.Lighting;

namespace RaptorSense.Hardware;

/// <summary>The light bar's own animations. Values are the firmware's (docs/protocol.md, "Light bar").</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]   // readable in profile files: "Mode": "Wave"
public enum LightBarMode : byte
{
    Breathing = 1, Neon = 2, Rainbow = 3, Wave = 4, Ripple = 5, Scanner = 6, Strobe = 7,
}

/// <summary>The rear light bar (three zones). Needs administrator rights, so it lives behind the helper.</summary>
public interface ILightBar
{
    /// <summary>One colour per zone (left, centre, right), brightness 0..100.</summary>
    void SetStatic(IReadOnlyList<Rgb> zones, int brightness);

    /// <summary>A built-in animation in one colour for the whole bar.</summary>
    void SetMode(LightBarMode mode, Rgb color, int speed, int brightness);
}

/// <summary>
/// Byte layouts for the light bar's WMI calls. Confirmed for static colours on a PH16-71 (2026-11-05); the animation
/// layout comes from jma-studio-rgb-keyboard (MIT) and has not been checked on this laptop yet.
/// </summary>
public static class LightBarProtocol
{
    public const int Zones = 3;
    public const int MaxBrightness = 100;
    public const int MinSpeed = 0, MaxSpeed = 10, DefaultSpeed = 5;

    /// <summary>The LED call's payload is the same every time; it triggers the update.</summary>
    public static readonly byte[] Trigger = [0x10, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x15, 0, 0];

    /// <summary>
    /// <c>SetGamingRgbKb</c> argument for one zone (0 = left, 1 = centre, 2 = right):
    /// <c>(r &lt;&lt; 8) | (g &lt;&lt; 16) | (b &lt;&lt; 24) | (0x08 &lt;&lt; 32) | (mask &lt;&lt; 40)</c> with masks 1, 2, 4.
    /// </summary>
    public static ulong ZoneColor(int zone, Rgb color)
    {
        if (zone is < 0 or >= Zones) throw new ArgumentOutOfRangeException(nameof(zone));
        return ((ulong)color.R << 8) | ((ulong)color.G << 16) | ((ulong)color.B << 24) | (0x08UL << 32) | ((ulong)(1 << zone) << 40);
    }

    /// <summary>The 16-byte <c>SetGamingKBBacklight</c> payload that accompanies a static colour.</summary>
    public static byte[] StaticBacklight(int brightness) =>
        [0, 0, ClampBrightness(brightness), 0, 0, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0];

    /// <summary>The 16-byte <c>SetGamingKBBacklight</c> payload that starts an animation.</summary>
    public static byte[] ModeBacklight(LightBarMode mode, Rgb color, int speed, int brightness) =>
        [(byte)mode, (byte)Math.Clamp(speed, MinSpeed, MaxSpeed), ClampBrightness(brightness), 0, 1, color.R, color.G, color.B, 3, 2, 0, 0, 0, 0, 0, 0];

    static byte ClampBrightness(int brightness) => (byte)Math.Clamp(brightness, 0, MaxBrightness);
}
