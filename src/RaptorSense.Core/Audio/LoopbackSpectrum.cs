using System.Diagnostics;
using System.Numerics;
using System.Runtime.Versioning;
using NAudio.Wave;

namespace RaptorSense.Audio;

/// <summary>
/// Captures what Windows is playing (WASAPI loopback, no microphone) and turns it into frequency bands.
/// Gain adjusts itself, so quiet and loud music both fill the bars.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LoopbackSpectrum : IAudioSpectrum
{
    const int FftSize = 2048;
    const double MinHz = 40, MaxHz = 16000, BassMaxHz = 150;

    // Loopback delivers no data while nothing plays; after this long the bars fall to zero.
    const long SilenceMs = 200;

    readonly WasapiLoopbackCapture capture;
    readonly object bufferLock = new();
    readonly float[] ring = new float[FftSize];
    readonly Complex[] fft = new Complex[FftSize];
    readonly double[] window = new double[FftSize];
    readonly Stopwatch sinceData = Stopwatch.StartNew();
    readonly int sampleRate;
    int ringPos;
    float[] smoothed = [];
    double peak = 1e-6;

    public float Bass { get; private set; }

    public LoopbackSpectrum()
    {
        capture = new WasapiLoopbackCapture();
        sampleRate = capture.WaveFormat.SampleRate;
        for (int i = 0; i < FftSize; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
        capture.DataAvailable += OnData;
        capture.StartRecording();
    }

    void OnData(object? sender, WaveInEventArgs e)
    {
        var format = capture.WaveFormat;
        int channels = format.Channels;
        int bytesPerSample = format.BitsPerSample / 8;
        int frameBytes = bytesPerSample * channels;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || (format.Encoding == WaveFormatEncoding.Extensible && bytesPerSample == 4);

        lock (bufferLock)
        {
            for (int offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                {
                    int at = offset + ch * bytesPerSample;
                    sum += isFloat ? BitConverter.ToSingle(e.Buffer, at) : BitConverter.ToInt16(e.Buffer, at) / 32768f;
                }
                ring[ringPos] = sum / channels;
                ringPos = (ringPos + 1) % FftSize;
            }
            if (e.BytesRecorded > 0) sinceData.Restart();
        }
    }

    public float[] GetBands(int count)
    {
        if (smoothed.Length != count) smoothed = new float[count];

        bool silent;
        lock (bufferLock)
        {
            silent = sinceData.ElapsedMilliseconds > SilenceMs;
            for (int i = 0; i < FftSize; i++) fft[i] = new Complex(ring[(ringPos + i) % FftSize] * window[i], 0);
        }

        var raw = new double[count];
        if (!silent)
        {
            Fft(fft);
            double binHz = (double)sampleRate / FftSize;
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
        double bassSum = 0;
        int bassBands = 0;
        for (int band = 0; band < count; band++)
        {
            float target = peak < 1e-4 ? 0 : (float)Math.Sqrt(Math.Clamp(raw[band] / peak, 0, 1));
            // Fast attack, slower release, so bars snap up on beats and fall smoothly.
            smoothed[band] = target > smoothed[band] ? smoothed[band] * 0.3f + target * 0.7f : smoothed[band] * 0.85f + target * 0.15f;
            if (MinHz * Math.Pow(MaxHz / MinHz, (band + 1) / (double)count) <= BassMaxHz * 1.5) { bassSum += smoothed[band]; bassBands++; }
        }
        Bass = bassBands > 0 ? (float)(bassSum / bassBands) : smoothed[0];
        return (float[])smoothed.Clone();
    }

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

    public void Dispose()
    {
        capture.DataAvailable -= OnData;
        capture.StopRecording();
        capture.Dispose();
    }
}
