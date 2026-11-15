using System.Diagnostics;
using Avalonia.Threading;
using RaptorSense.Audio;
using RaptorSense.Automation;
using RaptorSense.Profiles;

namespace RaptorSense.App;

/// <summary>
/// Watches what the rules depend on and applies them. To stay light it only watches what the current rules need, gets
/// told about changes by Windows where it can, only checks whether an app has closed while one of the named apps is
/// open, and only looks at the idle time when a rule could be about to start or end.
/// </summary>
sealed class AutomationRunner : IDisposable
{
    static readonly TimeSpan ClosedAppCheck = TimeSpan.FromSeconds(5);
    static readonly TimeSpan SoundCheck = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(2);

    readonly AutomationController controller;
    readonly AppTracker tracker = new(IsRunning);
    readonly DispatcherTimer closedApps, soundTimer, idleTimer;
    readonly SoundDetector sound = new();
    ForegroundAppWatcher? foreground;
    PowerSourceWatcher? powerSource;
    ScreenWatcher? screen;
    OutputMeter? meter;
    List<TimeSpan> idleLimits = [];
    TimeSpan idle;
    bool onBattery, screenOff;
    string? saveProblem;

    public AutomationRunner(IAutomationTarget target)
    {
        controller = new AutomationController(target);
        controller.Applied += () => Changed?.Invoke();
        closedApps = new DispatcherTimer(ClosedAppCheck, DispatcherPriority.Background, (_, _) =>
        {
            if (tracker.Sweep()) Evaluate();
        });
        closedApps.Stop();
        soundTimer = new DispatcherTimer(SoundCheck, DispatcherPriority.Background, (_, _) => CheckSound());
        soundTimer.Stop();
        idleTimer = new DispatcherTimer(IdleSchedule.WhileIdle, DispatcherPriority.Background, (_, _) => CheckIdle());
        idleTimer.Stop();
    }

    public AutomationSettings Settings { get; private set; } = AutomationStore.Load();

    public AutomationState State => new(tracker.Open, onBattery, sound.Playing, idle, screenOff);

    /// <summary>The lighting to save as the user's own setup while a rule is showing its lighting, or null.</summary>
    public Profile? LightingBefore => controller.LightingBefore;

    public string? LastProblem => saveProblem ?? controller.LastProblem;

    /// <summary>Raised on the UI thread when the state or the result of a change is new.</summary>
    public event Action? Changed;

    /// <summary>Raised when automations as a whole are switched on or off.</summary>
    public event Action? EnabledChanged;

    public void Start() => Refresh();

    /// <summary>Saves new settings and acts on them straight away.</summary>
    public void Update(AutomationSettings settings)
    {
        bool enabledChanged = settings.Enabled != Settings.Enabled;
        Settings = settings;
        try
        {
            AutomationStore.Save(settings);
            saveProblem = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            saveProblem = $"Could not save the rules: {e.Message}";
        }
        Refresh();
        if (enabledChanged) EnabledChanged?.Invoke();
    }

    void Refresh()
    {
        var rules = Settings.Enabled ? Settings.Rules.Where(r => r.Enabled).ToList() : [];
        var apps = rules.Where(r => r.Trigger == AutomationTrigger.AppOpen && r.AppName.Length > 0).Select(r => r.AppName).ToList();
        bool needsPowerSource = rules.Any(r => r.Trigger is AutomationTrigger.OnBattery or AutomationTrigger.PluggedIn);
        bool needsSound = rules.Any(r => r.Trigger == AutomationTrigger.SoundPlaying);
        bool needsScreen = rules.Any(r => r.Trigger == AutomationTrigger.ScreenOff);
        idleLimits = rules.Where(r => r.Trigger == AutomationTrigger.Idle).Select(r => r.IdleAfter).Distinct().ToList();

        if (OperatingSystem.IsWindows())
        {
            if (apps.Count > 0 && foreground is null)
            {
                foreground = new ForegroundAppWatcher();
                foreground.AppInFront += app => Dispatcher.UIThread.Post(() =>
                {
                    if (tracker.NoticeFront(app)) Evaluate();
                });
            }
            else if (apps.Count == 0 && foreground is not null)
            {
                foreground.Dispose();
                foreground = null;
            }

            if (needsPowerSource && powerSource is null)
            {
                onBattery = PowerSourceWatcher.IsOnBattery;
                powerSource = new PowerSourceWatcher();
                powerSource.Changed += battery => Dispatcher.UIThread.Post(() =>
                {
                    if (battery == onBattery) return;
                    onBattery = battery;
                    Evaluate();
                });
            }
            else if (!needsPowerSource && powerSource is not null)
            {
                powerSource.Dispose();
                powerSource = null;
            }

            if (needsScreen && screen is null)
            {
                screen = new ScreenWatcher();   // tells the current state a moment later; until then the screen counts as on
                screen.Changed += off => Dispatcher.UIThread.Post(() =>
                {
                    if (off == screenOff || screen is null) return;
                    screenOff = off;
                    Evaluate();
                });
            }
            else if (!needsScreen && screen is not null)
            {
                screen.Dispose();
                screen = null;
                screenOff = false;
            }

            if (needsSound && meter is null)
            {
                meter = new OutputMeter();
                soundTimer.Start();
            }
            else if (!needsSound && meter is not null)
            {
                soundTimer.Stop();
                meter.Dispose();
                meter = null;
                sound.Reset();
            }

            if (idleLimits.Count > 0) CheckIdle(evaluate: false);
            else
            {
                idleTimer.Stop();
                idle = TimeSpan.Zero;
            }
        }

        tracker.Watch(apps);
        Evaluate();
    }

    void CheckSound()
    {
        if (meter is not null && OperatingSystem.IsWindows() && sound.Update(meter.Peak(), SoundCheck)) Evaluate();
    }

    // Looks at the idle time only when it matters: at the moment the first rule could start, then four times a second
    // until the user is back.
    void CheckIdle(bool evaluate = true)
    {
        if (!OperatingSystem.IsWindows() || idleLimits.Count == 0) return;
        int reachedBefore = idleLimits.Count(limit => idle >= limit);
        idle = InputIdle.Time;
        idleTimer.Interval = IdleSchedule.NextCheck(idle, idleLimits);
        idleTimer.Start();
        if (evaluate && idleLimits.Count(limit => idle >= limit) != reachedBefore) Evaluate();
    }

    void Evaluate()
    {
        closedApps.IsEnabled = tracker.Open.Count > 0;
        var rules = Settings.Enabled ? Settings.Rules : [];
        _ = controller.RequestAsync(AutomationOutcome.Decide(rules, State));
        Changed?.Invoke();
    }

    static bool IsRunning(string app)
    {
        var processes = Process.GetProcessesByName(app);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    /// <summary>Stops watching and puts the power mode and fans back. Waits briefly: the app is quitting.</summary>
    public void Dispose()
    {
        closedApps.Stop();
        soundTimer.Stop();
        idleTimer.Stop();
        if (OperatingSystem.IsWindows())
        {
            foreground?.Dispose();
            powerSource?.Dispose();
            screen?.Dispose();
            meter?.Dispose();
        }
        // On a pool thread: the calls continue without needing the UI thread that is waiting here.
        try { Task.Run(controller.RestoreHelperSettingsAsync).Wait(QuitWait); }
        catch (AggregateException) { }
    }
}
