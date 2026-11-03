using RaptorSense.Audio;

namespace RaptorSense.Lighting;

/// <summary>Settings every effect reads; changed live by the UI.</summary>
public sealed class EffectSettings
{
    public Rgb Color { get; set; } = Rgb.PredatorTeal;
    public Rgb Color2 { get; set; } = new(255, 0, 128);

    /// <summary>Animation speed multiplier, 1 = normal.</summary>
    public double Speed { get; set; } = 1.0;
}

/// <param name="Time">Animation time in seconds, already multiplied by the speed setting.</param>
/// <param name="RealTime">Wall-clock seconds since the engine started.</param>
public readonly record struct EffectContext(double Time, double RealTime, double DeltaTime, EffectSettings Settings, IAudioSpectrum? Audio);

public interface IEffect
{
    string Id { get; }

    string Name { get; }

    bool UsesAudio => false;

    /// <summary>Draws into <paramref name="frame"/>, which starts black.</summary>
    void Render(Frame frame, in EffectContext ctx);
}
