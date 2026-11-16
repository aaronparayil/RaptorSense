using System.Diagnostics;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>
/// Plays the Lighting tab's effects on a Nitro's 4-zone keyboard. Each zone shows the average of its keys in the
/// engine's frame. A zone update goes through the helper and takes several firmware calls, so frames go a few times a
/// second, and only when the colours change.
/// </summary>
sealed class ZoneStreamer : IDisposable
{
    static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    const long KeepAliveMs = 2000;      // re-sent this often, so the look comes back after the Keyboard zones tab or a sleep
    const int RetryAfterFailureMs = 3000;
    const int SmallestChange = 3;       // per channel: closer than this doesn't need another update

    readonly EffectEngine engine;
    readonly Func<bool> enabled;
    readonly CancellationTokenSource stop = new();
    readonly Task loop;

    public ZoneStreamer(EffectEngine engine, Func<bool> enabled)
    {
        this.engine = engine;
        this.enabled = enabled;
        loop = Task.Run(() => RunAsync(stop.Token));
    }

    /// <summary>What the info panel shows in place of the per-key keyboard's status.</summary>
    public string Status { get; private set; } = "Starting";

    /// <summary>Zone updates sent in the last second.</summary>
    public double SendFps { get; private set; }

    /// <summary>Sends the next frame even if it hasn't changed (after something else changed the keyboard).</summary>
    public void Resend() => Interlocked.Exchange(ref resend, 1);

    int resend;

    async Task RunAsync(CancellationToken cancel)
    {
        using var timer = new PeriodicTimer(Interval);
        var frame = new Frame();
        Rgb[]? last = null;
        long lastSentMs = 0, statsStartMs = Environment.TickCount64;
        int sent = 0;

        try
        {
            while (await timer.WaitForNextTickAsync(cancel))
            {
                long now = Environment.TickCount64;
                if (now - statsStartMs >= 1000)
                {
                    SendFps = sent * 1000.0 / (now - statsStartMs);
                    sent = 0;
                    statsStartMs = now;
                }

                if (!enabled())
                {
                    last = null;
                    Status = "Preview only";
                    continue;
                }
                if (engine.Effect is KeyboardEffect)
                {
                    last = null;
                    Status = "That effect is built into the Helios's keyboard; pick another one for the Nitro.";
                    continue;
                }

                engine.CopyLatestFrame(frame);
                var zones = FourZoneLayout.Average(frame.Cells);
                bool force = Interlocked.Exchange(ref resend, 0) == 1;
                if (!force && last is not null && Close(zones, last) && now - lastSentMs < KeepAliveMs) continue;

                var watch = Stopwatch.StartNew();
                var status = await ThermalClient.SetKeyboardAsync(new FourZoneRequest
                {
                    Colors = zones.Select(z => z.ToString()).ToList(),
                    Confirm = false,
                }, cancel);
                if (status.Ok)
                {
                    last = zones;
                    lastSentMs = Environment.TickCount64;
                    sent++;
                    Status = $"Playing on the 4 keyboard zones ({watch.ElapsedMilliseconds} ms per update)";
                }
                else
                {
                    last = null;
                    Status = status.Reading is not null && status.HelperRevision < ThermalProtocol.FourZoneRevision
                        ? "The helper is from an older RaptorSense. Update it on the Performance tab to light this keyboard."
                        : status.Error ?? ThermalClient.HelperMissing;
                    await Task.Delay(RetryAfterFailureMs, cancel);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    static bool Close(Rgb[] a, Rgb[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (Math.Abs(a[i].R - b[i].R) >= SmallestChange || Math.Abs(a[i].G - b[i].G) >= SmallestChange ||
                Math.Abs(a[i].B - b[i].B) >= SmallestChange) return false;
        }
        return true;
    }

    public void Dispose()
    {
        stop.Cancel();
        try { loop.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        stop.Dispose();
    }
}
