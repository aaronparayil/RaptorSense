namespace RaptorSense.Hardware;

/// <summary>One top-level collection of a HID interface: what Windows shows as a device of its own.</summary>
public readonly record struct HidCollection(ushort UsagePage, ushort Usage);

/// <summary>
/// What a HID report descriptor says about an interface: its top-level collections and how long its reports are.
/// Lengths are payload bytes, without the report-id byte, and are the longest of each kind.
/// </summary>
public sealed record HidReportDescriptor(
    IReadOnlyList<HidCollection> Collections, bool UsesReportIds, int InputBytes, int OutputBytes, int FeatureBytes)
{
    public bool Has(ushort usagePage) => Collections.Any(c => c.UsagePage == usagePage);

    /// <summary>
    /// Reads a raw report descriptor, as Linux gives it in <c>/sys/class/hidraw/*/device/report_descriptor</c>.
    /// Windows parses these itself; on Linux it is the only way to tell an interface's collections apart.
    /// A damaged descriptor gives what could be read up to the damage.
    /// </summary>
    public static HidReportDescriptor Parse(ReadOnlySpan<byte> data)
    {
        const int Main = 0, Global = 1, Local = 2;
        const int Input = 8, Output = 9, Collection = 10, Feature = 11, EndCollection = 12;

        var collections = new List<HidCollection>();
        var bits = new Dictionary<(int Kind, uint Id), long>();
        var saved = new Stack<(uint Page, uint Size, uint Count, uint Id)>();
        uint page = 0, size = 0, count = 0, id = 0, usage = 0;
        bool usageSeen = false, usesIds = false;
        int depth = 0;

        for (int i = 0; i < data.Length;)
        {
            byte prefix = data[i++];
            if (prefix == 0xFE)   // long item: length, tag, data
            {
                if (i >= data.Length) break;
                i += 2 + data[i];
                continue;
            }
            int length = (prefix & 0x3) == 3 ? 4 : prefix & 0x3;
            if (i + length > data.Length) break;
            uint value = 0;
            for (int b = 0; b < length; b++) value |= (uint)data[i + b] << (8 * b);
            i += length;
            int type = (prefix >> 2) & 0x3, tag = prefix >> 4;

            if (type == Global)
            {
                switch (tag)
                {
                    case 0: page = value; break;
                    case 7: size = value; break;
                    case 8: id = value; usesIds = true; break;
                    case 9: count = value; break;
                    case 10: saved.Push((page, size, count, id)); break;
                    case 11: if (saved.Count > 0) (page, size, count, id) = saved.Pop(); break;
                }
            }
            else if (type == Local)
            {
                // Only the first usage before a collection names it. A 4-byte usage carries its page in the top half.
                if (tag == 0 && !usageSeen)
                {
                    usage = value;
                    if (length == 4) page = value >> 16;
                    usageSeen = true;
                }
            }
            else if (type == Main)
            {
                if (tag == Collection)
                {
                    if (depth++ == 0) collections.Add(new HidCollection((ushort)page, (ushort)usage));
                }
                else if (tag == EndCollection) depth = Math.Max(0, depth - 1);
                else if (tag is Input or Output or Feature)
                {
                    bits[(tag, id)] = bits.GetValueOrDefault((tag, id)) + (long)size * count;
                }
                usageSeen = false;   // local items end with each main item
            }
        }

        int Longest(int kind) => bits.Where(b => b.Key.Kind == kind).Select(b => (int)((b.Value + 7) / 8)).DefaultIfEmpty(0).Max();
        return new HidReportDescriptor(collections, usesIds, Longest(Input), Longest(Output), Longest(Feature));
    }
}
