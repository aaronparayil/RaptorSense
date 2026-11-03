using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace RaptorSense.App;

static class Program
{
    const string InstanceMutexName = @"Local\RaptorSense.App";
    const string ShowEventName = @"Local\RaptorSense.Show";

    [STAThread]
    public static int Main(string[] args)
    {
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
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
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
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
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

        var open = new NativeMenuItem("Open RaptorSense");
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
        if (profiles.Items.Count == 0) profiles.Add(new NativeMenuItem("(no saved profiles)") { IsEnabled = false });
        menu.Add(new NativeMenuItem("Profiles") { Menu = profiles });

        var pause = new NativeMenuItem("Pause lighting") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = !window.OutputEnabled };
        pause.Click += (_, _) => window.OutputEnabled = !window.OutputEnabled;
        menu.Add(pause);

        if (Autostart.IsSupported)
        {
            var autostart = new NativeMenuItem("Start with Windows") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.StartWithWindows };
            autostart.Click += (_, _) => window.StartWithWindows = !window.StartWithWindows;
            menu.Add(autostart);
        }

        menu.Add(new NativeMenuItemSeparator());
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        menu.Add(quit);

        tray.Menu = menu;
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
