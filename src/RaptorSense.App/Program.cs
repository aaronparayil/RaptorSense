using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
            else AskOverPipe();
            return 0;
        }

        // Named events are Windows-only. Elsewhere the first copy listens on a pipe (App.WatchShowRequests): on a
        // Linux desktop without a tray, starting RaptorSense again is the only way back to a closed window.
        using var showRequests = OperatingSystem.IsWindows() ? new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName) : null;
        App.ShowRequests = showRequests;
        App.StartHidden = args.Contains(Autostart.MinimizedArgument);
        return BuildAvaloniaApp(AppearanceSettings.UseGraphicsCard).StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>The pipe a second launch connects to, off Windows. Only this user can open it.</summary>
    public const string ShowPipeName = "RaptorSense.Show";

    // Connecting is the whole message. If the first copy isn't listening (it is still starting, or shutting down), give up quietly.
    static void AskOverPipe()
    {
        try
        {
            using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", ShowPipeName, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.CurrentUserOnly);
            pipe.Connect(1500);
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { }
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
        return useGraphicsCard ? builder : builder
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
            .With(new X11PlatformOptions { RenderingMode = [X11RenderingMode.Software] });
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

        var automations = new NativeMenuItem("Automations") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.AutomationsEnabled };
        automations.Click += (_, _) => window.AutomationsEnabled = !window.AutomationsEnabled;
        menu.Add(automations);

        if (Autostart.IsSupported)
        {
            var autostart = new NativeMenuItem(Autostart.OffLabel) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = window.StartWithWindows };
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
        if (ShowRequests is { } signal)
        {
            new Thread(() =>
            {
                while (signal.WaitOne()) Dispatcher.UIThread.Post(() => window?.ShowFromTray());
            })
            { IsBackground = true, Name = "RaptorSense show requests" }.Start();
        }
        else if (!OperatingSystem.IsWindows())
        {
            new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var pipe = new System.IO.Pipes.NamedPipeServerStream(Program.ShowPipeName, System.IO.Pipes.PipeDirection.In, 1,
                            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.CurrentUserOnly);
                        pipe.WaitForConnection();
                        Dispatcher.UIThread.Post(() => window?.ShowFromTray());
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        Thread.Sleep(2000);   // the name is taken or the folder isn't writable: try again later, quietly
                    }
                }
            })
            { IsBackground = true, Name = "RaptorSense show requests" }.Start();
        }
    }
}
