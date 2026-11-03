namespace RaptorSense.Audio;

/// <summary>Live analysis of the audio the computer is playing.</summary>
public interface IAudioSpectrum : IDisposable
{
    /// <summary>Levels 0..1 for <paramref name="count"/> log-spaced bands from low to high frequency.</summary>
    float[] GetBands(int count);

    /// <summary>Bass level 0..1, from the most recent <see cref="GetBands"/> call.</summary>
    float Bass { get; }
}
