using System.Diagnostics;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RaptorSense.Audio;

/// <summary>
/// Captures what Windows is playing (WASAPI loopback, no microphone) and hands it to a <see cref="SpectrumAnalyzer"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LoopbackSpectrum : IAudioSpectrum
{
    // Loopback delivers no data while nothing plays; after this long the bars fall to zero.
    const long SilenceMs = 200;

    readonly QuickLoopbackCapture capture;
    readonly SpectrumAnalyzer analyzer;
    readonly Stopwatch sinceData = Stopwatch.StartNew();
    float[] mono = [];   // only the capture thread touches it

    public float Bass => analyzer.Bass;

    public float Level => analyzer.Level;

    public LoopbackSpectrum()
    {
        capture = new QuickLoopbackCapture();
        analyzer = new SpectrumAnalyzer(capture.WaveFormat.SampleRate);
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
        if (frameBytes <= 0 || e.BytesRecorded <= 0) return;

        int frames = e.BytesRecorded / frameBytes;
        if (mono.Length < frames) mono = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            float sum = 0;
            for (int ch = 0; ch < channels; ch++)
            {
                int at = frame * frameBytes + ch * bytesPerSample;
                sum += isFloat ? BitConverter.ToSingle(e.Buffer, at) : BitConverter.ToInt16(e.Buffer, at) / 32768f;
            }
            mono[frame] = sum / channels;
        }
        analyzer.Add(mono.AsSpan(0, frames));
        lock (sinceData) sinceData.Restart();
    }

    public float[] GetBands(int count)
    {
        bool silent;
        lock (sinceData) silent = sinceData.ElapsedMilliseconds > SilenceMs;
        return analyzer.GetBands(count, silent);
    }

    public void Dispose()
    {
        capture.DataAvailable -= OnData;
        capture.StopRecording();
        capture.Dispose();
    }

    /// <summary>
    /// Loopback capture that hands sound over about 50 times a second. NAudio's own asks Windows for a 100 ms
    /// buffer and so delivers about 16 times a second, which is too coarse to catch a drum hit on time.
    /// </summary>
    sealed class QuickLoopbackCapture() : WasapiCapture(WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice(), false, BufferMs)
    {
        const int BufferMs = 30;

        protected override AudioClientStreamFlags GetAudioClientStreamFlags() => AudioClientStreamFlags.Loopback;
    }
}
