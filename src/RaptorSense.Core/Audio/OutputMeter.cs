using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;

namespace RaptorSense.Audio;

/// <summary>
/// How loud the PC's sound output is right now, from the level meter Windows keeps for it (the one behind the volume
/// mixer's green bars). Nothing is captured: it is one number, with no way to tell what is playing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class OutputMeter : IDisposable
{
    // Headphones plugged in or pulled out change which output is the default one, so look it up again now and then.
    const int ReadsPerLookup = 20;

    readonly MMDeviceEnumerator enumerator = new();
    MMDevice? device;
    int reads;

    /// <summary>The loudest sample of the last hundredth of a second, 0..1. Zero with no sound output at all.</summary>
    public float Peak()
    {
        try
        {
            if (device is null || reads++ >= ReadsPerLookup)
            {
                device?.Dispose();
                device = null;
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                reads = 0;
            }
            return device.AudioMeterInformation.MasterPeakValue;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or InvalidOperationException)
        {
            device?.Dispose();
            device = null;   // unplugged or switched off: look again next time
            return 0;
        }
    }

    public void Dispose()
    {
        device?.Dispose();
        enumerator.Dispose();
    }
}
