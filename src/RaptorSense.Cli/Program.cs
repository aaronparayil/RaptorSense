using RaptorSense.Audio;
using RaptorSense.Hardware;
using RaptorSense.Input;
using RaptorSense.Lighting;

const string Usage = """
    RaptorSense command line

      raptorsense info                     Find the keyboard and show its brightness
      raptorsense list                     List effects
      raptorsense run <effect> [options]   Run a software effect until Ctrl+C
      raptorsense builtin <name> [options] Switch to one of the keyboard's own effects

    Options:
      --color RRGGBB      main colour
      --color2 RRGGBB     second colour (spectrum top, pulse peak)
      --speed N           run: speed multiplier (1 = normal); builtin: 1 slowest .. 12 fastest
      --brightness N      0..50 (default: keyboard's current level)
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
    foreach (var e in Effects.All)
        Console.WriteLine($"  {e.Id,-10} {e.Name}{(e.UsesAudio ? "  (reacts to music)" : e.UsesKeys ? "  (reacts to typing)" : "")}");
    Console.WriteLine("\nKeyboard effects (raptorsense builtin <name>):");
    Console.WriteLine("  " + string.Join(", ", Enum.GetNames<BuiltInEffect>().Select(n => n.ToLowerInvariant())));
    return 0;
}

static int Run(string id, Dictionary<string, string> options)
{
    var effect = Effects.Find(id) ?? throw new ArgumentException($"Unknown effect '{id}'. Try: raptorsense list");
    using var engine = new EffectEngine(Ph16Keyboard.TryOpen) { Effect = effect };
    ApplySettings(engine.Settings, options);
    if (options.TryGetValue("brightness", out var b)) engine.Brightness = Math.Clamp(int.Parse(b), 0, Ph16Keyboard.MaxBrightness);
    if (options.TryGetValue("fps", out var fps)) engine.TargetFps = Math.Clamp(int.Parse(fps), 1, 240);

    IAudioSpectrum? audio = null;
    if (effect.UsesAudio)
    {
        if (!OperatingSystem.IsWindows()) throw new ArgumentException("Music effects are only implemented for Windows so far.");
        audio = new LoopbackSpectrum();
        engine.Audio = audio;
    }

    IKeyPressSource? keys = null;
    if (effect.UsesKeys)
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

static void ApplySettings(EffectSettings settings, Dictionary<string, string> options)
{
    if (options.TryGetValue("color", out var c)) settings.Color = Rgb.Parse(c);
    if (options.TryGetValue("color2", out var c2)) settings.Color2 = Rgb.Parse(c2);
    if (options.TryGetValue("speed", out var s)) settings.Speed = double.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
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
