using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RaptorSense.Audio;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using IEffect = RaptorSense.Lighting.IEffect;

namespace RaptorSense.App;

/// <summary>
/// A small keyboard playing one effect, for the gallery. Each frame is one little picture: a map of which key every
/// pixel belongs to, shared by all the previews, is filled in with the key colours and drawn in one go. That keeps a
/// screenful of moving previews cheap. It plays only when the gallery asks (<see cref="Advance"/>), which it does only
/// for the ones on screen, and <see cref="Release"/> gives its picture and effect back.
/// </summary>
sealed class EffectThumbnail : Control
{
    const int Gap = -1, Outside = -2;
    const uint PlateColour = 0xFF080816, OffKeyColour = 0xFF181634;   // 0xAARRGGBB: dark blue plate, dim violet key

    // One pixel map per size in use: almost always just one.
    static readonly Dictionary<(int W, int H), int[]> Maps = [];

    readonly IEffect shown;
    readonly bool zoned;
    readonly Frame frame = new();
    readonly Rgb[] cells = new Rgb[Frame.CellCount];
    readonly uint[] colours = new uint[Frame.CellCount];
    readonly Rgb[] lastShown = new Rgb[Frame.CellCount];
    IEffect? copy;
    WriteableBitmap? picture;
    uint[]? pixels;
    double time, realTime;

    /// <param name="zoned">For the Nitro: show each zone in the average of its keys, as that keyboard lights.</param>
    public EffectThumbnail(IEffect effect, bool zoned)
    {
        shown = effect;
        this.zoned = zoned;
        Height = 64;
    }

    /// <summary>Moves the effect on by <paramref name="seconds"/> and redraws.</summary>
    /// <param name="playing">What the real keyboard shows, used when this is the effect playing and has no copy of its own.</param>
    public void Advance(double seconds, EffectSettings settings, DemoAudio audio, IReadOnlyList<int> presses, Frame? playing)
    {
        // Each preview has its own copy, made the first time it is seen, so it never disturbs the one on the keyboard.
        copy ??= Effects.CreateCopy(shown);
        time += seconds * settings.Speed;
        realTime += seconds;
        frame.Fill(Rgb.Black);
        if (copy is not null)
        {
            try { copy.Render(frame, new EffectContext(time, realTime, seconds, settings, audio, presses)); }
            catch (Exception) { }   // a broken preview stays dark rather than taking the window down
        }
        else if (shown == Effects.Painted) Effects.Painted.Render(frame, new EffectContext(time, realTime, seconds, settings, null, []));
        else if (playing is not null) playing.CopyTo(frame);
        else foreach (int cell in KeyGeometry.Cells) frame[cell] = Rgb.FromHsv(260 + KeyGeometry.U(cell) * 80, 0.6, 0.35);   // "Your animation", not playing

        if (zoned) FourZoneLayout.Spread(FourZoneLayout.Average(frame.Cells), cells);
        else frame.Cells.CopyTo(cells);
        // A still effect (a static colour, a layout) is drawn once, not every frame.
        if (picture is not null && cells.AsSpan().SequenceEqual(lastShown)) return;
        cells.CopyTo(lastShown, 0);
        Paint();
    }

    /// <summary>True while it holds a picture and an effect copy, which <see cref="Release"/> gives back.</summary>
    public bool HoldsPicture => picture is not null || copy is not null;

    /// <summary>When it was last moved on, in the gallery's seconds.</summary>
    public double LastSeen { get; set; }

    /// <summary>Lets go of the picture and the effect's copy; the next <see cref="Advance"/> starts afresh.</summary>
    public void Release()
    {
        picture?.Dispose();
        picture = null;
        pixels = null;
        Array.Clear(lastShown);
        copy = null;
        time = realTime = 0;
        InvalidateVisual();
    }

    void Paint()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        int width = (int)Math.Round(Bounds.Width * scaling), height = (int)Math.Round(Bounds.Height * scaling);
        if (width < 8 || height < 8) return;
        if (picture is null || picture.PixelSize.Width != width || picture.PixelSize.Height != height)
        {
            picture?.Dispose();
            picture = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            pixels = new uint[width * height];
        }
        var map = MapFor(width, height);
        for (int cell = 0; cell < Frame.CellCount; cell++)
        {
            var c = cells[cell];
            colours[cell] = c == Rgb.Black ? OffKeyColour : 0xFF000000u | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
        }
        var target = pixels!;
        for (int i = 0; i < target.Length; i++)
        {
            int key = map[i];
            target[i] = key >= 0 ? colours[key] : key == Gap ? PlateColour : 0;
        }
        using (var buffer = picture.Lock())
            for (int y = 0; y < height; y++)
                Marshal.Copy((int[])(object)target, y * width, buffer.Address + y * buffer.RowBytes, width);
        InvalidateVisual();
    }

    /// <summary>For each pixel: the key under it, <see cref="Gap"/> between keys, or <see cref="Outside"/> past the rounded plate.</summary>
    static int[] MapFor(int width, int height)
    {
        if (Maps.TryGetValue((width, height), out var map)) return map;
        map = new int[width * height];
        double padX = width * 0.03, padY = height * 0.08;
        double scale = Math.Min((width - 2 * padX) / PhysicalLayout.Width, (height - 2 * padY) / PhysicalLayout.Height);
        double left = (width - PhysicalLayout.Width * scale) / 2, top = (height - PhysicalLayout.Height * scale) / 2;
        double plateRadius = Math.Min(width, height) * 0.14, keyRadius = 7 * scale;
        Array.Fill(map, Gap);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (!InsideRounded(x + 0.5, y + 0.5, 0, 0, width, height, plateRadius)) map[y * width + x] = Outside;
        foreach (var key in KeyMap.Keys)
        {
            if (PhysicalLayout.For(key.Label) is not { } r) continue;
            double x0 = left + r.X * scale, y0 = top + r.Y * scale, w = r.Width * scale, h = r.Height * scale;
            for (int y = (int)y0; y <= (int)(y0 + h) && y < height; y++)
                for (int x = (int)x0; x <= (int)(x0 + w) && x < width; x++)
                    if (InsideRounded(x + 0.5, y + 0.5, x0, y0, w, h, keyRadius)) map[y * width + x] = key.Cell;
        }
        Maps[(width, height)] = map;
        return map;
    }

    static bool InsideRounded(double px, double py, double x, double y, double w, double h, double radius)
    {
        if (px < x || py < y || px >= x + w || py >= y + h) return false;
        double dx = Math.Max(Math.Max(x + radius - px, px - (x + w - radius)), 0);
        double dy = Math.Max(Math.Max(y + radius - py, py - (y + h - radius)), 0);
        return dx * dx + dy * dy <= radius * radius;
    }

    public override void Render(DrawingContext context)
    {
        // The picture has a pixel for every screen pixel: all of it, shrunk to the control's size in layout units.
        if (picture is not null) context.DrawImage(picture, new Rect(picture.Size), new Rect(Bounds.Size));
        else context.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 8, 22)), null, new RoundedRect(new Rect(Bounds.Size), 9));
    }
}
