using System.Diagnostics;
using Avalonia.Threading;
using RaptorSense.Automation;
using RaptorSense.Profiles;

namespace RaptorSense.App;

/// <summary>
/// Watches what the rules depend on and applies them. To stay light it only watches what the current rules need, gets
/// told about changes by Windows, and only checks whether an app has closed while one of the named apps is open.
/// </summary>
sealed class AutomationRunner : IDisposable
{
    static readonly TimeSpan ClosedAppCheck = TimeSpan.FromSeconds(5);
    static readonly TimeSpan QuitWait = TimeSpan.FromSeconds(2);

    readonly AutomationController controller;
    readonly AppTracker tracker = new(IsRunning);
    readonly DispatcherTimer closedApps;
    ForegroundAppWatcher? foreground;
    PowerSourceWatcher? powerSource;
    bool onBattery;
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
    }

    public AutomationSettings Settings { get; private set; } = AutomationStore.Load();

    public AutomationState State => new(tracker.Open, onBattery);

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
        }

        tracker.Watch(apps);
        Evaluate();
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
        if (OperatingSystem.IsWindows())
        {
            foreground?.Dispose();
            powerSource?.Dispose();
        }
        // On a pool thread: the calls continue without needing the UI thread that is waiting here.
        try { Task.Run(controller.RestoreHelperSettingsAsync).Wait(QuitWait); }
        catch (AggregateException) { }
    }
}
