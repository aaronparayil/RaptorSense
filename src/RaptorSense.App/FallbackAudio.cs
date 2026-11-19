using System.Diagnostics;
using RaptorSense.Audio;

namespace RaptorSense.App;

/// <summary>
/// For the gallery's showcase: the music the computer is playing, or, while it is quiet, the demo beat, so the music
/// effects still have something to show. It switches back to the real sound as soon as any plays.
/// </summary>
sealed class FallbackAudio(IAudioSpectrum? real) : IAudioSpectrum
{
    const float Quiet = 0.03f;
    const double QuietSeconds = 1.5;

    readonly DemoAudio demo = new();
    readonly Stopwatch clock = Stopwatch.StartNew();
    double lastHeard = double.NegativeInfinity;
    bool usingReal;

    /// <summary>True while the demo beat is standing in for silence.</summary>
    public bool Demo => !usingReal;

    public float Bass => usingReal ? real!.Bass : demo.Bass;

    public float Level => usingReal ? real!.Level : demo.Level;

    public float[] GetBands(int count)
    {
        double now = clock.Elapsed.TotalSeconds;
        if (real is not null)
        {
            var bands = real.GetBands(count);
            if (real.Level > Quiet) lastHeard = now;
            usingReal = now - lastHeard < QuietSeconds;
            if (usingReal) return bands;
        }
        demo.Time = now;
        return demo.GetBands(count);
    }

    public void Dispose() { }   // the real capture belongs to the window
}
