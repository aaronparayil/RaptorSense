namespace RaptorSense.Lighting;

/// <summary>One colour per lighting cell of the keyboard.</summary>
public sealed class Frame
{
    public const int CellCount = 128;

    readonly Rgb[] cells = new Rgb[CellCount];

    public Rgb this[int cell]
    {
        get => cells[cell];
        set => cells[cell] = value;
    }

    public ReadOnlySpan<Rgb> Cells => cells;

    public void Fill(Rgb color) => Array.Fill(cells, color);

    public void CopyTo(Frame other) => cells.CopyTo(other.cells, 0);

    public bool SameAs(Frame other) => cells.AsSpan().SequenceEqual(other.cells);
}
