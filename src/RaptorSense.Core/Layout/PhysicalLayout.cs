namespace RaptorSense.Layout;

/// <summary>Where a key sits on the keyboard, in layout units (one key pitch = <see cref="PhysicalLayout.Pitch"/>).</summary>
public readonly record struct KeyRect(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    public bool Contains(double x, double y) => x >= X && x <= X + Width && y >= Y && y <= Y + Height;
}

/// <summary>
/// Physical shape and position of every key on the PH16-71 keyboard, measured from the key layout:
/// staggered rows, wide modifiers, half-height F row and the numpad. Effects use it for real distances.
/// </summary>
public static class PhysicalLayout
{
    /// <summary>Distance between neighbouring keys in the main block.</summary>
    public const double Pitch = 64.3;

    const double KeyW = 52, SmallW = 46, FW = 45, RowH = 52, FRowH = 34;
    const double FRow = 0, Row1 = 48, Row2 = 114, Row3 = 180, Row4 = 246, Row5 = 312;
    static readonly double[] NumpadX = [967, 1025, 1082, 1139];

    static readonly Dictionary<string, KeyRect> Rects = Build();

    public static double Width { get; } = Rects.Values.Max(r => r.X + r.Width);
    public static double Height { get; } = Rects.Values.Max(r => r.Y + r.Height);

    static readonly KeyRect?[] ByCell = BuildByCell();

    public static KeyRect? For(string label) => Rects.TryGetValue(label, out var r) ? r : null;

    public static KeyRect? ForCell(int cell) => cell is >= 0 and < Lighting.Frame.CellCount ? ByCell[cell] : null;

    static Dictionary<string, KeyRect> Build()
    {
        var r = new Dictionary<string, KeyRect>(StringComparer.OrdinalIgnoreCase);
        void Add(string label, double x, double y, double w, double h) => r[label] = new KeyRect(x, y, w, h);
        void Run(IEnumerable<string> labels, double startX, double y)
        {
            int i = 0;
            foreach (var label in labels) Add(label, startX + i++ * Pitch, y, KeyW, RowH);
        }

        // F row: four groups, with gaps between them.
        string[] fRow = ["Esc", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "PrtSc", "Ins", "Del"];
        double[] fX = [0, 69, 126, 183, 240, 309, 366, 423, 480, 549, 606, 663, 720, 789, 846, 903];
        for (int i = 0; i < fRow.Length; i++) Add(fRow[i], fX[i], FRow, i == 0 ? 47 : FW, FRowH);
        string[] media = ["Prev", "Play", "Next", "Power"];
        for (int i = 0; i < media.Length; i++) Add(media[i], NumpadX[i], FRow, FW, FRowH);

        // Number row
        Add("`", 0, Row1, 50, RowH);
        Run(["1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "="], 57, Row1);
        Add("Bksp", 828, Row1, 117, RowH);

        // Top letter row
        Add("Tab", 0, Row2, 75, RowH);
        Run(["Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]"], 87, Row2);
        Add("\\", 859, Row2, 86, RowH);

        // Home row
        Add("Caps", 0, Row3, 87, RowH);
        Run(["A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'"], 97, Row3);
        Add("Enter", 805, Row3, 140, RowH);

        // Bottom letter row
        Add("Shift", 0, Row4, 117, RowH);
        Run(["Z", "X", "C", "V", "B", "N", "M", ",", ".", "/"], 129, Row4);
        Add("RShift", 773, Row4, 112, RowH);
        Add("Up", 897, Row4, 50, RowH);

        // Space row: the right arrow sits under the first numpad column.
        Run(["Ctrl", "Fn", "Win", "Alt"], 0, Row5);
        Add("Space", 257, Row5, 312, RowH);
        Run(["AltGr", "Menu"], 580, Row5);
        Add("RCtrl", 709, Row5, 110, RowH);
        Add("Left", 832, Row5, 50, RowH);
        Add("Down", 897, Row5, 50, RowH);
        Add("Right", 961, Row5, 50, RowH);

        // Numpad
        string[][] numpad =
        [
            ["Predator", "NumLk", "Num/", "Num*"],
            ["Num7", "Num8", "Num9", "Num-"],
            ["Num4", "Num5", "Num6", "Num+"],
            ["Num1", "Num2", "Num3"],
        ];
        double[] numpadY = [Row1, Row2, Row3, Row4];
        for (int row = 0; row < numpad.Length; row++)
            for (int col = 0; col < numpad[row].Length; col++) Add(numpad[row][col], NumpadX[col], numpadY[row], SmallW, RowH);
        Add("NumEnt", NumpadX[3], Row4, SmallW, Row5 + RowH - Row4);
        Add("Num0", NumpadX[1], Row5, SmallW, RowH);
        Add("Num.", NumpadX[2], Row5, SmallW, RowH);
        return r;
    }

    static KeyRect?[] BuildByCell()
    {
        var cells = new KeyRect?[Lighting.Frame.CellCount];
        foreach (var key in KeyMap.Keys) cells[key.Cell] = For(key.Label);
        return cells;
    }
}
