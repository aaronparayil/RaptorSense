namespace RaptorSense.Layout;

/// <summary>
/// PC scan codes (set 1, as Windows reports them) to <see cref="KeyMap"/> labels. Scan codes name physical keys,
/// so this works with any keyboard layout (QWERTY, AZERTY, ...). Numpad keys report the same codes with NumLock on or off.
/// </summary>
public static class ScanCodes
{
    static readonly Dictionary<int, string> Normal = new()
    {
        [0x01] = "Esc", [0x02] = "1", [0x03] = "2", [0x04] = "3", [0x05] = "4", [0x06] = "5", [0x07] = "6",
        [0x08] = "7", [0x09] = "8", [0x0A] = "9", [0x0B] = "0", [0x0C] = "-", [0x0D] = "=", [0x0E] = "Bksp",
        [0x0F] = "Tab", [0x10] = "Q", [0x11] = "W", [0x12] = "E", [0x13] = "R", [0x14] = "T", [0x15] = "Y",
        [0x16] = "U", [0x17] = "I", [0x18] = "O", [0x19] = "P", [0x1A] = "[", [0x1B] = "]", [0x1C] = "Enter",
        [0x1D] = "Ctrl", [0x1E] = "A", [0x1F] = "S", [0x20] = "D", [0x21] = "F", [0x22] = "G", [0x23] = "H",
        [0x24] = "J", [0x25] = "K", [0x26] = "L", [0x27] = ";", [0x28] = "'", [0x29] = "`", [0x2A] = "Shift",
        [0x2B] = "\\", [0x2C] = "Z", [0x2D] = "X", [0x2E] = "C", [0x2F] = "V", [0x30] = "B", [0x31] = "N",
        [0x32] = "M", [0x33] = ",", [0x34] = ".", [0x35] = "/", [0x36] = "RShift", [0x37] = "Num*", [0x38] = "Alt",
        [0x39] = "Space", [0x3A] = "Caps",
        [0x3B] = "F1", [0x3C] = "F2", [0x3D] = "F3", [0x3E] = "F4", [0x3F] = "F5", [0x40] = "F6",
        [0x41] = "F7", [0x42] = "F8", [0x43] = "F9", [0x44] = "F10", [0x57] = "F11", [0x58] = "F12",
        [0x45] = "NumLk", [0x47] = "Num7", [0x48] = "Num8", [0x49] = "Num9", [0x4A] = "Num-", [0x4B] = "Num4",
        [0x4C] = "Num5", [0x4D] = "Num6", [0x4E] = "Num+", [0x4F] = "Num1", [0x50] = "Num2", [0x51] = "Num3",
        [0x52] = "Num0", [0x53] = "Num.",
    };

    // Keys whose scan code comes with the E0 prefix (Windows' "extended" flag).
    static readonly Dictionary<int, string> Extended = new()
    {
        [0x10] = "Prev", [0x19] = "Next", [0x1C] = "NumEnt", [0x1D] = "RCtrl", [0x22] = "Play", [0x35] = "Num/",
        [0x37] = "PrtSc", [0x38] = "AltGr", [0x45] = "NumLk", [0x48] = "Up", [0x4B] = "Left", [0x4D] = "Right",
        [0x50] = "Down", [0x52] = "Ins", [0x53] = "Del", [0x5B] = "Win", [0x5C] = "Win", [0x5D] = "Menu",
    };

    /// <summary>The lighting cell for a key, or null when the key has no cell (or is not on this keyboard).</summary>
    public static int? CellFor(int scanCode, bool extended)
    {
        var table = extended ? Extended : Normal;
        return table.TryGetValue(scanCode, out var label) ? KeyMap.Find(label)?.Cell : null;
    }
}
