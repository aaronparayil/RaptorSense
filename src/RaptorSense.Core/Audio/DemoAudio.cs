namespace RaptorSense.Audio;

/// <summary>
/// Pretend music: a steady 120 beats a minute with a kick on every beat, a softer middle and hi-hats in between.
/// The effect gallery's previews and the tests use it, so music effects can be seen without anything playing.
/// It follows <see cref="Time"/>, which the caller moves on; nothing here reads a clock.
/// </summary>
public sealed class DemoAudio : IAudioSpectrum
{
    const double BeatSeconds = 0.5;

    float bass, level;

    /// <summary>Seconds of "music" so far.</summary>
    public double Time { get; set; }

    public float Bass => bass;

    public float Level => level;

    public float[] GetBands(int count)
    {
        var bands = new float[Math.Max(count, 0)];
        double beat = Time / BeatSeconds;
        double sinceKick = (beat - Math.Floor(beat)) * BeatSeconds;
        double kick = Math.Exp(-sinceKick / 0.12);
        double sinceHat = ((beat + 0.5) - Math.Floor(beat + 0.5)) * BeatSeconds;
        double hat = Math.Exp(-sinceHat / 0.05);
        // A melody that wanders through the middle bands.
        double melody = 0.5 + 0.5 * Math.Sin(Time * 1.3);

        for (int i = 0; i < bands.Length; i++)
        {
            double f = bands.Length == 1 ? 0 : i / (double)(bands.Length - 1);   // 0 = lowest, 1 = highest
            double low = kick * Math.Exp(-f * 6);
            double mid = 0.45 * Math.Exp(-Math.Pow((f - 0.25 - 0.35 * melody) / 0.18, 2)) * (0.75 + 0.25 * Math.Sin(Time * 7 + i));
            double high = hat * 0.6 * Math.Max(0, f - 0.55) / 0.45;
            double floor = 0.06 + 0.04 * Math.Sin(Time * 3.1 + i * 1.7);
            bands[i] = (float)Math.Clamp(low + mid + high + floor, 0, 1);
        }
        bass = (float)Math.Clamp(kick * 0.95 + 0.05, 0, 1);
        level = (float)Math.Clamp(0.35 + 0.4 * kick + 0.15 * melody, 0, 1);
        return bands;
    }

    public void Dispose() { }
}
