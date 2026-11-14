using System.Numerics;

namespace RaptorSense.Audio;

/// <summary>
/// Turns a stream of mono samples into frequency bands. Gain adjusts itself, so quiet and loud music both fill the
/// bars. It knows nothing about where the sound comes from, so it can be fed made-up audio in tests.
/// </summary>
public sealed class SpectrumAnalyzer
{
    const int FftSize = 2048;
    const double MinHz = 40, MaxHz = 16000, BassMaxHz = 150;

    // The quietest the bass's own loudest may be, as a share of the whole spectrum's loudest.
    const double BassFloor = 0.1;

    readonly object bufferLock = new();
    readonly float[] ring = new float[FftSize];
    readonly Complex[] fft = new Complex[FftSize];
    readonly double[] window = new double[FftSize];
    readonly int sampleRate;
    int ringPos;
    float[] smoothed = [];
    double peak = 1e-6, bassPeak = 1e-6;
    float bass;

    public SpectrumAnalyzer(int sampleRate)
    {
        this.sampleRate = sampleRate;
        for (int i = 0; i < FftSize; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
    }

    /// <summary>Bass level 0..1, from the most recent <see cref="GetBands"/> call.</summary>
    public float Bass { get; private set; }

    /// <summary>Overall level 0..1 (the average band), from the most recent <see cref="GetBands"/> call.</summary>
    public float Level { get; private set; }

    /// <summary>Adds the newest samples. Safe to call from the capture thread while another thread reads bands.</summary>
    public void Add(ReadOnlySpan<float> samples)
    {
        lock (bufferLock)
        {
            foreach (float sample in samples)
            {
                ring[ringPos] = sample;
                ringPos = (ringPos + 1) % FftSize;
            }
        }
    }

    /// <summary>Levels 0..1 for <paramref name="count"/> log-spaced bands. <paramref name="silent"/> lets the bars fall when no audio is arriving.</summary>
    public float[] GetBands(int count, bool silent = false)
    {
        if (smoothed.Length != count) smoothed = new float[count];

        lock (bufferLock)
        {
            for (int i = 0; i < FftSize; i++) fft[i] = new Complex(ring[(ringPos + i) % FftSize] * window[i], 0);
        }

        var raw = new double[count];
        double rawBass = 0;
        if (!silent)
        {
            Fft(fft);
            double binHz = (double)sampleRate / FftSize;
            int lastBassBin = Math.Max(1, (int)(BassMaxHz / binHz));
            for (int bin = Math.Max(1, (int)(MinHz / binHz)); bin <= lastBassBin; bin++) rawBass = Math.Max(rawBass, fft[bin].Magnitude);
            for (int band = 0; band < count; band++)
            {
                double lo = MinHz * Math.Pow(MaxHz / MinHz, band / (double)count);
                double hi = MinHz * Math.Pow(MaxHz / MinHz, (band + 1) / (double)count);
                int first = Math.Max(1, (int)(lo / binHz));
                int last = Math.Max(first, Math.Min(FftSize / 2 - 1, (int)(hi / binHz)));
                double max = 0;
                for (int bin = first; bin <= last; bin++) max = Math.Max(max, fft[bin].Magnitude);
                raw[band] = max * (1 + 1.5 * band / count);   // music has less energy up high; tilt so treble shows
            }
        }

        double loudest = raw.Max();
        peak = Math.Max(peak * 0.997, loudest);   // slow automatic gain
        double sum = 0;
        for (int band = 0; band < count; band++)
        {
            float target = peak < 1e-4 ? 0 : (float)Math.Sqrt(Math.Clamp(raw[band] / peak, 0, 1));
            smoothed[band] = Follow(smoothed[band], target);
            sum += smoothed[band];
        }
        Level = count > 0 ? (float)(sum / count) : 0;

        // The bass is measured against its own recent loudest, not the whole spectrum's: in most music the middle
        // is louder than the bass drum, and the drum would hardly register. The floor keeps music with no real
        // bass from having its rumble blown up to full.
        bassPeak = Math.Max(bassPeak * 0.997, rawBass);
        double bassGain = Math.Max(bassPeak, peak * BassFloor);
        bass = Follow(bass, bassGain < 1e-4 ? 0 : (float)Math.Sqrt(Math.Clamp(rawBass / bassGain, 0, 1)));
        Bass = bass;
        return (float[])smoothed.Clone();
    }

    // Fast attack, slower release, so levels snap up on beats and fall smoothly.
    static float Follow(float current, float target) => target > current ? current * 0.3f + target * 0.7f : current * 0.85f + target * 0.15f;

    // In-place iterative radix-2 FFT.
    static void Fft(Complex[] data)
    {
        int n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            var step = Complex.FromPolarCoordinates(1, -2 * Math.PI / len);
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int k = 0; k < len / 2; k++)
                {
                    var u = data[i + k];
                    var v = data[i + k + len / 2] * w;
                    data[i + k] = u + v;
                    data[i + k + len / 2] = u - v;
                    w *= step;
                }
            }
        }
    }
}
