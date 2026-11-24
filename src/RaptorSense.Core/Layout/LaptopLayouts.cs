using RaptorSense.Hardware;
using RaptorSense.Lighting;
using RaptorSense.Thermal;

using static RaptorSense.Localization.Text;

namespace RaptorSense.Layout;

/// <summary>Which keyboard the app draws and drives. One app for all: it picks by the laptop model Windows reports and the keyboard found.</summary>
public enum LaptopLayout
{
    /// <summary>Predator Helios 16 PH16-71: every key lit on its own (USB keyboard 04F2:0117).</summary>
    Helios,

    /// <summary>Nitro 5 AN515-57: four lighting zones, left to right, through the gaming firmware.</summary>
    Nitro,

    /// <summary>
    /// Four lighting zones on any other laptop: on an ENE controller (<see cref="EneKeyboard"/>), or through the firmware
    /// after its owner tried it (<see cref="FourZoneTrial"/>).
    /// </summary>
    FourZones,
}

public static class LaptopLayouts
{
    /// <summary>
    /// The Nitro's layout on a laptop with a checked 4-zone keyboard, four zones where they were found or tried,
    /// and the Helios's everywhere else.
    /// </summary>
    public static LaptopLayout For(string? model, bool fourZones = false) =>
        LaptopModels.HasFourZoneKeyboard(model) ? LaptopLayout.Nitro
        : fourZones && !LaptopModels.IsVerified(model) ? LaptopLayout.FourZones
        : LaptopLayout.Helios;

    /// <summary>True when the keyboard lights in four zones rather than key by key.</summary>
    public static bool HasZones(this LaptopLayout layout) => layout is LaptopLayout.Nitro or LaptopLayout.FourZones;

    /// <summary>The short name the app shows: "Currently on Nitro". Other 4-zone keyboards are on laptops of both names.</summary>
    public static string ShortName(LaptopLayout layout, string? model = null) => layout switch
    {
        LaptopLayout.Nitro => "Nitro",
        LaptopLayout.FourZones => IsNitro(model) ? "Nitro" : "Predator",
        _ => "Helios",
    };

    /// <summary>Key caps to change from the Helios's, or null.</summary>
    public static IReadOnlyDictionary<string, string>? CapsFor(LaptopLayout layout, string? model) =>
        layout == LaptopLayout.Nitro || (layout == LaptopLayout.FourZones && IsNitro(model)) ? NitroCaps : null;

    static bool IsNitro(string? model) => model?.Contains("Nitro", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>A line under the name: what the keyboard can do, and the model when it isn't the one the layout was drawn from.</summary>
    public static string Describe(LaptopLayout layout, string? model) => layout switch
    {
        LaptopLayout.Nitro or LaptopLayout.FourZones => $"{LaptopModels.Describe(model)} · " + L("4 lighting zones"),
        _ when LaptopModels.IsVerified(model) => $"{LaptopModels.Describe(model)} · " + L("every key lit on its own"),
        _ => L("Helios 16 layout · this laptop: {0}", LaptopModels.Describe(model)),
    };

    /// <summary>Key caps printed differently on the Nitro (labels as in <see cref="KeyMap"/>).</summary>
    public static IReadOnlyDictionary<string, string> NitroCaps { get; } = new Dictionary<string, string>
    {
        ["Predator"] = "Nitro",   // the NitroSense key above the numpad
    };
}

/// <summary>
/// How the Nitro 5 AN515-57's four zones lie under the keys, by each key's position in <see cref="PhysicalLayout"/>. Used for
/// every 4-zone keyboard: nobody has measured the others yet.
/// Effects still render per key; a zone shows the average of its keys. The boundaries are where they look to be on
/// the laptop: the main block in three equal parts, then the arrows and the numpad.
/// </summary>
public static class FourZoneLayout
{
    /// <summary>Left edge of zones 2, 3 and 4, in layout units. The main block is about 945 wide.</summary>
    public static readonly double[] ZoneStarts = [315, 630, 950];

    static readonly HashSet<string> ArrowKeys = new(["Up", "Down", "Left", "Right"], StringComparer.OrdinalIgnoreCase);

    static readonly int[] ZoneByCell = Build();

    /// <summary>The zone (0 = left .. 3) the key in this cell belongs to, or -1 for a cell with no key.</summary>
    public static int ZoneOf(int cell) => cell is >= 0 and < Frame.CellCount ? ZoneByCell[cell] : -1;

    /// <summary>The zone a key is in, by its label.</summary>
    public static int ZoneOf(Key key) => ZoneOf(key.Cell);

    /// <summary>One colour per zone: the average of the keys in it.</summary>
    public static Rgb[] Average(ReadOnlySpan<Rgb> cells)
    {
        Span<int> r = stackalloc int[FourZoneProtocol.Zones], g = stackalloc int[FourZoneProtocol.Zones],
            b = stackalloc int[FourZoneProtocol.Zones], n = stackalloc int[FourZoneProtocol.Zones];
        for (int cell = 0; cell < cells.Length && cell < Frame.CellCount; cell++)
        {
            int zone = ZoneByCell[cell];
            if (zone < 0) continue;
            r[zone] += cells[cell].R;
            g[zone] += cells[cell].G;
            b[zone] += cells[cell].B;
            n[zone]++;
        }
        var zones = new Rgb[FourZoneProtocol.Zones];
        for (int zone = 0; zone < zones.Length; zone++)
            zones[zone] = n[zone] == 0 ? Rgb.Black : new Rgb((byte)(r[zone] / n[zone]), (byte)(g[zone] / n[zone]), (byte)(b[zone] / n[zone]));
        return zones;
    }

    /// <summary>Paints every key with its zone's colour: how the Nitro's keyboard will look.</summary>
    public static void Spread(IReadOnlyList<Rgb> zones, Span<Rgb> cells)
    {
        for (int cell = 0; cell < cells.Length && cell < Frame.CellCount; cell++)
        {
            int zone = ZoneByCell[cell];
            cells[cell] = zone >= 0 && zone < zones.Count ? zones[zone] : Rgb.Black;
        }
    }

    /// <summary>The zone a key at this horizontal position (its centre, in layout units) belongs to.</summary>
    public static int ZoneAt(double centerX)
    {
        int zone = 0;
        while (zone < ZoneStarts.Length && centerX >= ZoneStarts[zone]) zone++;
        return zone;
    }

    static int[] Build()
    {
        var zones = new int[Frame.CellCount];
        Array.Fill(zones, -1);
        foreach (var key in KeyMap.Keys)
        {
            if (PhysicalLayout.For(key.Label) is not { } rect) continue;
            zones[key.Cell] = ArrowKeys.Contains(key.Label) ? FourZoneProtocol.Zones - 1 : ZoneAt(rect.CenterX);
        }
        return zones;
    }
}
