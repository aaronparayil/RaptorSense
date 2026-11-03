using RaptorSense.Audio;

namespace RaptorSense.Lighting;

/// <summary>Settings every effect reads; changed live by the UI.</summary>
public sealed class EffectSettings
{
    public Rgb Color { get; set; } = Rgb.PredatorTeal;
    public Rgb Color2 { get; set; } = new(255, 0, 128);

    /// <summary>Animation speed multiplier, 1 = normal.</summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>"Auto" colour: effects use flowing rainbow colours instead of colour 1.</summary>
    public bool AutoColor { get; set; }
}

/// <param name="Time">Animation time in seconds, already multiplied by the speed setting.</param>
/// <param name="RealTime">Wall-clock seconds since the engine started.</param>
/// <param name="Presses">Cells pressed since the previous frame.</param>
public readonly record struct EffectContext(
    double Time, double RealTime, double DeltaTime, EffectSettings Settings, IAudioSpectrum? Audio, IReadOnlyList<int> Presses)
{
    /// <summary>Colour 1, or with Auto colour a rainbow hue that flows across the keyboard (<paramref name="u"/> 0..1, left to right).</summary>
    public Rgb Primary(double u) => Settings.AutoColor ? Rgb.FromHsv(u * 300 + Time * 60) : Settings.Color;

    /// <summary>The colour typing effects draw with: colour 2, or with Auto colour the given hue.</summary>
    public Rgb Accent(double hue) => Settings.AutoColor ? Rgb.FromHsv(hue) : Settings.Color2;
}

public static class EffectCategory
{
    public const string Basic = "Basic", Animated = "Animated", Nature = "Nature", Music = "Music", Typing = "Typing";
    public const string Keyboard = "Built into the keyboard";

    public static IReadOnlyList<string> Order { get; } = [Basic, Animated, Nature, Music, Typing, Keyboard];
}

public interface IEffect
{
    string Id { get; }

    string Name { get; }

    /// <summary>Group in the effect picker.</summary>
    string Category => EffectCategory.Basic;

    bool UsesAudio => false;

    /// <summary>True when the effect reacts to key presses (needs a key-press source).</summary>
    bool UsesKeys => false;

    /// <summary>True when the effect draws only around key presses, on black, so it can be layered over another effect.</summary>
    bool WorksAsOverlay => false;

    /// <summary>Draws into <paramref name="frame"/>, which starts black.</summary>
    void Render(Frame frame, in EffectContext ctx);
}
