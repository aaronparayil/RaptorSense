using RaptorSense.Audio;
using RaptorSense.Hardware;
using RaptorSense.Input;
using RaptorSense.Lighting;
using RaptorSense.Profiles;
using RaptorSense.Thermal;

const string Usage = """
    RaptorSense command line

      raptorsense info                     Find the keyboard and show its brightness
      raptorsense list                     List effects
      raptorsense run <effect> [options]   Run a software effect until Ctrl+C
      raptorsense builtin <name> [options] Switch to one of the keyboard's own effects
      raptorsense profiles                 List saved profiles
      raptorsense profile <name> [options] Run a saved profile until Ctrl+C (options override it)
      raptorsense thermal                  Temperatures, fan speeds and power profile (needs the helper)
      raptorsense power <profile>          quiet | balanced | performance | turbo | eco
      raptorsense fans auto | max | custom <cpu%> <gpu%>
      raptorsense lightbar <RRGGBB> [<RRGGBB> <RRGGBB>]   Rear light bar: one colour, or left, centre, right (needs the helper)
      raptorsense lightbar off
      raptorsense lightbar <mode> [--color RRGGBB]        breathing | neon | rainbow | wave | ripple | scanner | strobe

    Options:
      --color RRGGBB      main colour
      --color2 RRGGBB     second colour (spectrum top, pulse peak)
      --speed N           run: speed multiplier (1 = normal); builtin: 1 slowest .. 12 fastest; lightbar mode: 0..10
      --brightness N      0..50 (default: keyboard's current level); lightbar: 0..100 (default 100)
      --auto on|off       run: rainbow "Auto" colours instead of --color
      --overlay <id>      run: a typing effect on top, e.g. --overlay typing-lightning (or none)
      --seconds N         run: stop after N seconds
      --fps N             run: target frame rate (default 60)
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    Console.WriteLine(RaptorSense.AppInfo.Credit);
    return 0;
}

try
{
    var options = ParseOptions(args.Skip(1).ToArray(), out var positional);
    switch (args[0])
    {
        case "info": return Info();
        case "list": return List();
        case "run": return Run(positional.FirstOrDefault() ?? "rainbow", options);
        case "builtin": return BuiltIn(positional.FirstOrDefault() ?? "wave", options);
        case "profiles": return ListProfiles();
        case "layout": return PrintLayout();
        case "thermal": return PrintThermal(ThermalClient.StatusAsync().GetAwaiter().GetResult());
        case "power": return SetPower(positional.FirstOrDefault());
        case "fans": return SetFans(positional);
        case "lightbar": return SetLightBar(positional, options);
        case "profile": return RunProfile(positional.FirstOrDefault(), options);
        default:
            Console.Error.WriteLine($"Unknown command '{args[0]}'.\n\n{Usage}");
            return 2;
    }
}
catch (Exception e) when (e is FormatException or ArgumentException)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}

static int Info()
{
    using var keyboard = OpenOrFail();
    if (keyboard is null) return 1;
    Console.WriteLine($"Keyboard: {keyboard.Description}");
    Console.WriteLine($"Brightness: {keyboard.GetBrightness()} / {Ph16Keyboard.MaxBrightness}");
    return 0;
}

static int List()
{
    Console.WriteLine("Software effects (raptorsense run <id>):");
    foreach (var group in Effects.All.GroupBy(e => e.Category))
    {
        Console.WriteLine($"  {group.Key}");
        foreach (var e in group)
            Console.WriteLine($"    {e.Id,-18} {e.Name}{(e.UsesAudio ? "  (music)" : e.WorksAsOverlay ? "  (typing, works as --overlay)" : e.UsesKeys ? "  (typing)" : "")}");
    }
    Console.WriteLine("\nKeyboard effects (raptorsense builtin <name>):");
    Console.WriteLine("  " + string.Join(", ", Enum.GetNames<BuiltInEffect>().Select(n => n.ToLowerInvariant())));
    return 0;
}

static int Run(string id, Dictionary<string, string> options)
{
    var effect = Effects.Find(id) ?? throw new ArgumentException($"Unknown effect '{id}'. Try: raptorsense list");
    using var engine = new EffectEngine(Ph16Keyboard.TryOpen) { Effect = effect };
    ApplySettings(engine, options);
    return RunEngine(engine, options);
}

static int RunProfile(string? name, Dictionary<string, string> options)
{
    if (name is null) throw new ArgumentException("Which profile? Try: raptorsense profiles");
    var profile = ProfileStore.Load(name) ?? throw new ArgumentException($"No profile named '{name}'. Try: raptorsense profiles");
    using var engine = new EffectEngine(Ph16Keyboard.TryOpen);
    profile.ApplyTo(engine);
    ApplySettings(engine, options);   // command-line options override the saved values

    if (profile.LightBar is { } lightBar && lightBar.TryGetZones(out _))
    {
        var status = ThermalClient.SetLightBarAsync(lightBar).GetAwaiter().GetResult();
        if (!status.Ok) Console.Error.WriteLine($"Light bar not set: {status.Error ?? ThermalClient.HelperMissing}");
    }
    return RunEngine(engine, options);
}

static int PrintThermal(ThermalStatus status)
{
    if (status.Reading is not { } r)
    {
        Console.Error.WriteLine(status.Error ?? ThermalClient.HelperMissing);
        return 1;
    }
    Console.WriteLine($"CPU {r.CpuTemp} °C, GPU {(r.GpuTemp > 0 ? $"{r.GpuTemp} °C" : "asleep")}, system {r.SystemTemp} °C");
    Console.WriteLine($"Fans: CPU {r.CpuFanRpm} rpm, GPU {r.GpuFanRpm} rpm, mode {status.FanMode}" +
        (status.FanMode == FanMode.Custom ? $" ({status.CpuPercent}% / {status.GpuPercent}%)" : "") +
        (status.SafetyOverride ? "  [safety: held at Max]" : ""));
    Console.WriteLine($"Power profile: {status.Profile} (supported: {string.Join(", ", status.SupportedProfiles)})");
    if (!status.Ok || status.Error is not null) Console.Error.WriteLine(status.Error);
    return status.Ok ? 0 : 1;
}

static int SetPower(string? name)
{
    if (!Enum.TryParse<PowerProfile>(name, ignoreCase: true, out var profile))
        throw new ArgumentException($"Power profile: {string.Join(", ", Enum.GetNames<PowerProfile>()).ToLowerInvariant()}");
    return PrintThermal(ThermalClient.SetProfileAsync(profile).GetAwaiter().GetResult());
}

static int SetFans(List<string> args)
{
    if (!Enum.TryParse<FanMode>(args.FirstOrDefault(), ignoreCase: true, out var mode))
        throw new ArgumentException("Usage: raptorsense fans auto | max | custom <cpu%> <gpu%>");
    int cpu = 50, gpu = 50;
    if (mode == FanMode.Custom && (args.Count < 3 || !int.TryParse(args[1], out cpu) || !int.TryParse(args[2], out gpu)))
        throw new ArgumentException($"Usage: raptorsense fans custom <cpu%> <gpu%>  (at least {FanController.MinCustomPercent}%)");
    return PrintThermal(ThermalClient.SetFansAsync(mode, cpu, gpu).GetAwaiter().GetResult());
}

static int SetLightBar(List<string> args, Dictionary<string, string> options)
{
    const string LightBarUsage = "Usage: raptorsense lightbar <RRGGBB> [<RRGGBB> <RRGGBB>] | off | <mode> [--color RRGGBB] [--speed 0..10] [--brightness 0..100]";
    if (args.Count == 0) throw new ArgumentException(LightBarUsage);

    int brightness = options.TryGetValue("brightness", out var b) ? int.Parse(b) : LightBarProtocol.MaxBrightness;
    var request = new LightBarRequest { Brightness = brightness };
    if (args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
        request = request with { Colors = [Rgb.Black.ToString()] };
    else if (Enum.GetValues<LightBarMode>().Cast<LightBarMode?>().FirstOrDefault(m => m.ToString()!.Equals(args[0], StringComparison.OrdinalIgnoreCase)) is { } mode)
        request = request with
        {
            Mode = mode,
            Colors = [(options.TryGetValue("color", out var c) ? Rgb.Parse(c) : Rgb.White).ToString()],
            Speed = options.TryGetValue("speed", out var s) ? int.Parse(s) : LightBarProtocol.DefaultSpeed,
        };
    else
        request = request with { Colors = args.Select(a => Rgb.Parse(a).ToString()).ToList() };

    var status = ThermalClient.SetLightBarAsync(request).GetAwaiter().GetResult();
    if (status.Ok) Console.WriteLine("Light bar set.");
    else Console.Error.WriteLine(status.Error ?? ThermalClient.HelperMissing);
    return status.Ok ? 0 : 1;
}

// CSV of every key's cell and physical rectangle, for checking the layout or drawing it elsewhere.
static int PrintLayout()
{
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    Console.WriteLine("label,cell,x,y,width,height");
    foreach (var key in RaptorSense.Layout.KeyMap.Keys)
    {
        if (RaptorSense.Layout.PhysicalLayout.For(key.Label) is not { } r) continue;
        Console.WriteLine(string.Join(",", $"\"{key.Label.Replace("\"", "\"\"")}\"", key.Cell,
            r.X.ToString("0.#", inv), r.Y.ToString("0.#", inv), r.Width.ToString("0.#", inv), r.Height.ToString("0.#", inv)));
    }
    return 0;
}

static int ListProfiles()
{
    var names = ProfileStore.List();
    if (names.Count == 0) Console.WriteLine($"No profiles yet. Save one in the app; they are stored in {ProfileStore.Folder}");
    foreach (var name in names) Console.WriteLine($"  {name}");
    return 0;
}

static int RunEngine(EffectEngine engine, Dictionary<string, string> options)
{
    var effect = engine.Effect;
    if (options.TryGetValue("fps", out var fps)) engine.TargetFps = Math.Clamp(int.Parse(fps), 1, 240);

    IAudioSpectrum? audio = null;
    if (effect.UsesAudio)
    {
        if (!OperatingSystem.IsWindows()) throw new ArgumentException("Music effects are only implemented for Windows so far.");
        audio = new LoopbackSpectrum();
        engine.Audio = audio;
    }

    IKeyPressSource? keys = null;
    if (engine.NeedsKeys)
    {
        if (!OperatingSystem.IsWindows()) throw new ArgumentException("Reactive effects are only implemented for Windows so far.");
        keys = new WindowsKeyHook();
        keys.CellPressed += engine.NotifyKeyPress;
    }

    using var stop = new ManualResetEventSlim();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
    double seconds = options.TryGetValue("seconds", out var s) ? double.Parse(s) : double.PositiveInfinity;
    var deadline = DateTime.UtcNow + (double.IsInfinity(seconds) ? TimeSpan.MaxValue - TimeSpan.FromDays(1) : TimeSpan.FromSeconds(seconds));

    engine.Start();
    Console.WriteLine($"Running '{effect.Name}'. Ctrl+C to stop.");
    while (!stop.IsSet && DateTime.UtcNow < deadline)
    {
        stop.Wait(1000);
        Console.WriteLine($"  {engine.Status}  |  render {engine.RenderFps:N0} fps, sent {engine.SendFps:N0} fps, brightness {engine.Brightness}");
    }
    keys?.Dispose();
    engine.Stop();
    audio?.Dispose();
    return 0;
}

static int BuiltIn(string name, Dictionary<string, string> options)
{
    if (!Enum.TryParse<BuiltInEffect>(name, ignoreCase: true, out var effect))
        throw new ArgumentException($"Unknown keyboard effect '{name}'. Try: raptorsense list");
    using var keyboard = OpenOrFail();
    if (keyboard is null) return 1;
    var color = options.TryGetValue("color", out var c) ? Rgb.Parse(c) : Rgb.PredatorTeal;
    int speed = options.TryGetValue("speed", out var sp) ? int.Parse(sp) : 6;
    int brightness = options.TryGetValue("brightness", out var b) ? int.Parse(b) : keyboard.GetBrightness();
    keyboard.SetBuiltInEffect(effect, effect is BuiltInEffect.Wave or BuiltInEffect.Neon ? Rgb.Black : color, speed, brightness);
    Console.WriteLine($"Keyboard effect set: {effect}");
    return 0;
}

static Ph16Keyboard? OpenOrFail()
{
    var keyboard = Ph16Keyboard.TryOpen();
    if (keyboard is null) Console.Error.WriteLine("Keyboard lighting interface not found (expected USB 04F2:0117/011A/0119, usage page FF02).");
    return keyboard;
}

static void ApplySettings(EffectEngine engine, Dictionary<string, string> options)
{
    if (options.TryGetValue("color", out var c)) engine.Settings.Color = Rgb.Parse(c);
    if (options.TryGetValue("color2", out var c2)) engine.Settings.Color2 = Rgb.Parse(c2);
    if (options.TryGetValue("speed", out var s)) engine.Settings.Speed = double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
    if (options.TryGetValue("brightness", out var b)) engine.Brightness = Math.Clamp(int.Parse(b), 0, Ph16Keyboard.MaxBrightness);
    if (options.TryGetValue("auto", out var auto)) engine.Settings.AutoColor = auto is "on" or "true" or "1";
    if (options.TryGetValue("overlay", out var o))
        engine.Overlay = o == "none" ? null : Effects.Find(o) is { WorksAsOverlay: true } top ? top
            : throw new ArgumentException($"'{o}' can't be an overlay. Typing effects that can: " +
                string.Join(", ", Effects.All.Where(e => e.WorksAsOverlay).Select(e => e.Id)));
}

static Dictionary<string, string> ParseOptions(string[] args, out List<string> positional)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    positional = [];
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--")) { positional.Add(args[i]); continue; }
        if (i + 1 >= args.Length) throw new ArgumentException($"Option {args[i]} needs a value.");
        options[args[i][2..]] = args[++i];
    }
    return options;
}
