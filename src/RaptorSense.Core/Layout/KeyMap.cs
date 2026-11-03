namespace RaptorSense.Layout;

/// <summary>A key and the lighting cell under it. Row 0 is the bottom row (Ctrl, Space), row 5 the F-key row.</summary>
public sealed record Key(int Cell, string Label, bool Confirmed = true)
{
    public int Column => Cell / KeyMap.Rows;
    public int Row => Cell % KeyMap.Rows;
}

/// <summary>
/// Cell layout of the Predator Helios 16 PH16-71 keyboard (Chicony 04F2:0117): cell = column * 6 + row.
/// Derived from the verified key map in TrueSense (https://github.com/Order52/truesense, MIT licence).
/// </summary>
public static class KeyMap
{
    public const int Columns = 22;
    public const int Rows = 6;

    public static IReadOnlyList<Key> Keys { get; } =
    [
        new(0, "Ctrl"), new(1, "Shift"), new(2, "Caps"), new(3, "Tab"), new(4, "`"), new(5, "Esc"),
        new(6, "Fn"), new(10, "1"), new(11, "F1"),
        new(12, "Win"), new(13, "\\"), new(15, "Q"), new(16, "2"), new(17, "F2"),
        new(18, "Alt"), new(19, "Z"), new(20, "A"), new(21, "W"), new(22, "3"), new(23, "F3"),
        new(24, "Space"), new(25, "X"), new(26, "S"), new(27, "E"), new(28, "4"), new(29, "F4"),
        new(31, "C"), new(32, "D"), new(33, "R"), new(34, "5"), new(35, "F5"),
        new(37, "V"), new(38, "F"), new(39, "T"), new(40, "6"), new(41, "F6"),
        new(43, "B"), new(44, "G"), new(45, "Y"), new(46, "7"), new(47, "F7"),
        new(49, "N"), new(50, "H"), new(51, "U"), new(52, "8"), new(53, "F8"),
        new(55, "M"), new(56, "J"), new(57, "I"), new(58, "9"), new(59, "F9"),
        new(60, "AltGr"), new(61, ","), new(62, "K"), new(63, "O"), new(64, "0"), new(65, "F10"),
        new(67, "."), new(68, "L"), new(69, "P"), new(70, "-"), new(71, "F11"),
        new(72, "Menu"), new(73, "/"), new(74, ";"), new(75, "["), new(76, "="), new(77, "F12"),
        new(78, "RCtrl"), new(80, "'"), new(81, "]"), new(82, "Bksp"), new(83, "PrtSc"),
        new(84, "Left"), new(85, "RShift"), new(86, "Enter"), new(89, "Ins"),
        new(90, "Up"), new(95, "Del"),
        new(96, "Down"), new(99, "Num7"), new(100, "Num1"), new(101, "Num4"),
        new(102, "Right"), new(103, "Next"), new(104, "Prev"), new(105, "Play"), new(106, "Predator"), new(107, "Power"),
        new(108, "Num0"), new(109, "Num2"), new(110, "Num5"), new(111, "Num8"), new(112, "NumLk"),
        new(114, "Num."), new(115, "Num3"), new(116, "Num6"), new(117, "Num9"), new(118, "Num/"),
        new(120, "NumEnt"), new(122, "Num+"), new(123, "Num-"), new(124, "Num*"),
    ];

    static readonly Dictionary<string, Key> ByLabel = Keys.ToDictionary(k => k.Label, StringComparer.OrdinalIgnoreCase);

    public static Key? Find(string label) => ByLabel.GetValueOrDefault(label);
}
