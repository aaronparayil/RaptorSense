using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using RaptorSense.Localization;

namespace RaptorSense.App;

static class Program
{
    const string InstanceMutexName = @"Local\RaptorSense.App";
    const string ShowEventName = @"Local\RaptorSense.Show";

    /// <summary>Given with a process id by <see cref="App.Restart"/>: wait for that copy to end before starting.</summary>
    public const string AfterArgument = "--after";

    [STAThread]
    public static int Main(string[] args)
    {
        // A restart: the copy that started this one is still saving and letting go of the keyboard.
        int after = Array.IndexOf(args, AfterArgument);
        if (after >= 0 && after + 1 < args.Length && int.TryParse(args[after + 1], out int pid))
        {
            try { using var previous = System.Diagnostics.Process.GetProcessById(pid); previous.WaitForExit(15000); }
            catch (ArgumentException) { }   // already gone
        }

        // One copy only: two engines would fight over the keyboard. A second launch asks the first to show itself.
        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out bool first);
        if (!first)
        {
            if (OperatingSystem.IsWindows())
            {
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
            }
            return 0;
        }

        // Named events are Windows-only; elsewhere a second launch simply exits.
        using var showRequests = OperatingSystem.IsWindows() ? new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName) : null;
        App.ShowRequests = showRequests;
        App.StartHidden = args.Contains(Autostart.MinimizedArgument);
        // The chosen language, or the system's when none was chosen; read before any window is built.
        Text.Use(AppearanceSettings.Language ?? Text.FromSystem()?.Code);
        return BuildAvaloniaApp(AppearanceSettings.UseGraphicsCard).StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>
    /// The window is drawn on the processor unless <paramref name="useGraphicsCard"/>. Drawing with the graphics
    /// card costs about 100 MB of memory for as long as the app runs, even in the tray with no window to draw, and
    /// this window is small, flat and usually closed. The price is more processor time while the window is open and
    /// something on it moves.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp(bool useGraphicsCard = false)
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        return useGraphicsCard ? builder : builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
    }
}

sealed class App : Application
{
    public static EventWaitHandle? ShowRequests { get; set; }

    public static bool StartHidden { get; set; }

    MainWindow? window;
    TrayIcon? tray;

    static WindowIcon? icon;

    // Loaded on first use: a WindowIcon needs Avalonia's platform services, which exist only once the app has started.
    public static WindowIcon Icon => icon ??= new(typeof(App).Assembly.GetManifestResourceStream("RaptorSense.icon.png")!);
    public override void Initialize()
    {
        Look.Install(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            window = new MainWindow();
            window.TrayStateChanged += RebuildTrayMenu;
            if (!StartHidden) window.Show();

            tray = new TrayIcon { Icon = Icon, ToolTipText = AppInfo.Name };
            tray.Clicked += (_, _) => window.ShowFromTray();
            TrayIcon.SetIcons(this, [tray]);
            RebuildTrayMenu();

            desktop.ShutdownRequested += (_, _) => window.Shutdown();   // Windows sign-out or shutdown
            WatchShowRequests();
        }
        base.OnFrameworkInitializationCompleted();
    }

    void RebuildTrayMenu()
    {
        if (tray is null || window is null) return;
        var menu = new NativeMenu();

        var open = new NativeMenuItem(L("Open RaptorSense"));
        open.Click += (_, _) => window.ShowFromTray();
        menu.Add(open);
        menu.Add(new NativeMenuItemSeparator());

        var profiles = new NativeMenu();
        foreach (var name in window.Profiles)
        {
            var item = new NativeMenuItem(name);
            item.Click += (_, _) => window.LoadProfile(name);
            profiles.Add(item);
        }
        if (profiles.Items.Count == 0) profiles.Add(new NativeMenuItem(L("(no saved profiles)")) { IsEnabled = false });
        menu.Add(new NativeMenuItem(L("Profiles")) { Menu = profiles });

        var pause = new NativeMenuItem(L("Pause lighting")) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = !window.OutputEnabled };
        pause.Click += (_, _) => window.OutputEnabled = !window.OutputEnabled;
        menu.Add(pause);

        var automations = new NativeMenuItem(L("Automations")) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.AutomationsEnabled };
        automations.Click += (_, _) => window.AutomationsEnabled = !window.AutomationsEnabled;
        menu.Add(automations);

        if (Autostart.IsSupported)
        {
            var autostart = new NativeMenuItem(Autostart.OffLabel) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.StartWithWindows };
            autostart.Click += (_, _) => window.StartWithWindows = !window.StartWithWindows;
            menu.Add(autostart);
        }

        menu.Add(new NativeMenuItemSeparator());
        var quit = new NativeMenuItem(L("Quit"));
        quit.Click += (_, _) => Quit();
        menu.Add(quit);

        tray.Menu = menu;
    }

    /// <summary>Starts a fresh copy, which waits for this one to end, and quits. Used after the language changes.</summary>
    public static void Restart()
    {
        if (Environment.ProcessPath is { } path)
            System.Diagnostics.Process.Start(path, [Program.AfterArgument, Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        (Current as App)?.Quit();
    }

    void Quit()
    {
        window?.Shutdown();
        if (tray is not null) tray.IsVisible = false;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    // A second launch signals this event; show the window on the UI thread.
    void WatchShowRequests()
    {
        if (ShowRequests is not { } signal) return;
        new Thread(() =>
        {
            while (signal.WaitOne()) Dispatcher.UIThread.Post(() => window?.ShowFromTray());
        })
        { IsBackground = true, Name = "RaptorSense show requests" }.Start();
    }
}
