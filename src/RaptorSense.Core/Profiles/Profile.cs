using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Thermal;

namespace RaptorSense.Profiles;

/// <summary>A saved lighting setup. Stored as readable JSON so people can share and hand-edit them.</summary>
public sealed record Profile
{
    public string Name { get; init; } = "";

    /// <summary>Effect id, e.g. "custom", "ripple".</summary>
    public string Effect { get; init; } = "static";

    public string Color { get; init; } = Rgb.PredatorTeal.ToString();

    public string Color2 { get; init; } = "FF0080";

    public double Speed { get; init; } = 1;

    /// <summary>"Auto" colour: rainbow colours instead of colour 1.</summary>
    public bool AutoColor { get; init; }

    /// <summary>Id of a typing effect layered on top, or null.</summary>
    public string? Overlay { get; init; }

    /// <summary>0..50, or -1 for the keyboard's own level.</summary>
    public int Brightness { get; init; } = -1;

    /// <summary>Painted keys by label (see <see cref="KeyMap"/>), as RRGGBB. Keys not listed are off.</summary>
    public Dictionary<string, string> Keys { get; init; } = [];

    /// <summary>
    /// The rear light bar, or null to leave it alone. Null in profiles saved before the light bar was supported, and
    /// until the light bar has been set from RaptorSense, so a profile never overrides what Acer's own app set.
    /// </summary>
    public LightBarRequest? LightBar { get; init; }

    /// <summary>Snapshot of what the engine and the painted layout show now, plus the light bar if it has been set.</summary>
    public static Profile Capture(string name, EffectEngine engine, LightBarRequest? lightBar = null) => new()
    {
        Name = name,
        LightBar = lightBar,
        Effect = engine.Effect.Id,
        Color = engine.Settings.Color.ToString(),
        Color2 = engine.Settings.Color2.ToString(),
        Speed = engine.Settings.Speed,
        AutoColor = engine.Settings.AutoColor,
        Overlay = engine.Overlay?.Id,
        Brightness = engine.Brightness,
        Keys = KeyMap.Keys
            .Where(k => Effects.Painted[k.Cell] != Rgb.Black)
            .ToDictionary(k => k.Label, k => Effects.Painted[k.Cell].ToString()),
    };

    /// <summary>Loads this profile into the engine and the painted layout. Unknown keys and bad colours are skipped.</summary>
    public void ApplyTo(EffectEngine engine)
    {
        if (Rgb.TryParse(Color, out var c1)) engine.Settings.Color = c1;
        if (Rgb.TryParse(Color2, out var c2)) engine.Settings.Color2 = c2;
        engine.Settings.Speed = Math.Clamp(Speed, 0.1, 4);
        engine.Settings.AutoColor = AutoColor;
        if (Brightness >= 0) engine.Brightness = Math.Clamp(Brightness, 0, Hardware.Ph16Keyboard.MaxBrightness);

        Effects.Painted.Fill(Rgb.Black);
        foreach (var (label, hex) in Keys)
        {
            if (KeyMap.Find(label) is { } key && Rgb.TryParse(hex, out var color)) Effects.Painted[key.Cell] = color;
        }

        engine.Effect = Lighting.Effects.Find(Effect) ?? Lighting.Effects.Painted;
        engine.Overlay = Overlay is null ? null : Lighting.Effects.Find(Overlay) is { WorksAsOverlay: true } top ? top : null;
    }
}
