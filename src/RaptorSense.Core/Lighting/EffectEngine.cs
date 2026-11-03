using System.Diagnostics;
using RaptorSense.Audio;
using RaptorSense.Hardware;

namespace RaptorSense.Lighting;

/// <summary>
/// Renders the current effect at a steady rate on its own thread and streams changed frames to the keyboard.
/// Reconnects by itself when the keyboard disappears (e.g. after sleep).
/// </summary>
public sealed class EffectEngine(Func<Ph16Keyboard?> openKeyboard) : IDisposable
{
    const int ReconnectIntervalMs = 2000;

    // Unchanged frames are re-sent this often, so the look comes back if other software overwrites it.
    const int KeepAliveMs = 1000;

    readonly object frameLock = new();
    readonly Frame latest = new();
    Thread? thread;
    volatile bool running;
    volatile IEffect effect = Effects.All[0];
    Ph16Keyboard? keyboard;

    public EffectSettings Settings { get; } = new();

    public IEffect Effect { get => effect; set => effect = value; }

    public IAudioSpectrum? Audio { get; set; }

    /// <summary>0..50, or -1 to adopt the keyboard's own brightness when it connects.</summary>
    public int Brightness { get; set; } = -1;

    public int TargetFps { get; set; } = 60;

    /// <summary>False renders the preview only and leaves the keyboard alone.</summary>
    public bool OutputEnabled { get; set; } = true;

    public double RenderFps { get; private set; }

    public double SendFps { get; private set; }

    public string Status { get; private set; } = "Stopped";

    public bool Connected { get; private set; }

    public void CopyLatestFrame(Frame target)
    {
        lock (frameLock) latest.CopyTo(target);
    }

    public void Start()
    {
        if (running) return;
        running = true;
        thread = new Thread(Run) { IsBackground = true, Name = "RaptorSense effect engine", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    public void Stop()
    {
        running = false;
        thread?.Join();
        thread = null;
    }

    void Run()
    {
        using var timer = new PreciseTimer();
        var clock = Stopwatch.StartNew();
        var working = new Frame();
        var lastSent = new Frame();
        int lastSentBrightness = -1;
        double previous = 0, phase = 0, statsStart = 0, nextFrameAt = 0;
        long nextReconnectMs = 0, lastSendMs = long.MinValue / 2;
        int rendered = 0, sent = 0;

        while (running)
        {
            double now = clock.Elapsed.TotalSeconds;
            double delta = now - previous;
            previous = now;
            phase += delta * Settings.Speed;

            working.Fill(Rgb.Black);
            try { effect.Render(working, new EffectContext(phase, now, delta, Settings, Audio)); }
            catch (Exception e) { Status = $"Effect error: {e.Message}"; }
            lock (frameLock) working.CopyTo(latest);
            rendered++;

            if (OutputEnabled)
            {
                if (keyboard is null && clock.ElapsedMilliseconds >= nextReconnectMs)
                {
                    Connect();
                    nextReconnectMs = clock.ElapsedMilliseconds + ReconnectIntervalMs;
                }
                if (keyboard is not null)
                {
                    int brightness = Math.Max(Brightness, 0);
                    bool changed = brightness != lastSentBrightness || !working.SameAs(lastSent);
                    if (changed || clock.ElapsedMilliseconds - lastSendMs >= KeepAliveMs)
                    {
                        try
                        {
                            keyboard.SendFrame(working, brightness);
                            working.CopyTo(lastSent);
                            lastSentBrightness = brightness;
                            lastSendMs = clock.ElapsedMilliseconds;
                            sent++;
                        }
                        catch (Exception e)
                        {
                            Disconnect($"Lost the keyboard: {e.Message}");
                        }
                    }
                }
            }
            else if (keyboard is not null)
            {
                Disconnect("Preview only");
                lastSentBrightness = -1;
            }

            if (now - statsStart >= 1)
            {
                RenderFps = rendered / (now - statsStart);
                SendFps = sent / (now - statsStart);
                rendered = sent = 0;
                statsStart = now;
            }

            // Fixed frame times, so small overheads don't add up; after a long stall, start the schedule again.
            nextFrameAt = Math.Max(nextFrameAt + 1.0 / Math.Max(TargetFps, 1), clock.Elapsed.TotalSeconds - 0.1);
            timer.Wait(TimeSpan.FromSeconds(nextFrameAt - clock.Elapsed.TotalSeconds));
        }

        Disconnect("Stopped");
    }

    void Connect()
    {
        try
        {
            keyboard = openKeyboard();
            if (keyboard is null) { Status = "Keyboard not found"; return; }
            if (Brightness < 0)
            {
                int current = keyboard.GetBrightness();
                Brightness = current > 0 ? current : 25;
            }
            Connected = true;
            Status = $"Connected to {keyboard.Description}";
        }
        catch (Exception e)
        {
            Disconnect($"Could not open the keyboard: {e.Message}");
        }
    }

    void Disconnect(string status)
    {
        keyboard?.Dispose();
        keyboard = null;
        Connected = false;
        Status = status;
    }

    public void Dispose() => Stop();
}
