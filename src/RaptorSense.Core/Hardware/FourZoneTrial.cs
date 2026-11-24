using RaptorSense.Lighting;
using RaptorSense.Thermal;

namespace RaptorSense.Hardware;

/// <summary>What the owner saw when the test colours were sent.</summary>
public enum FourZoneSeen { AllRight, MixedUp, Nothing }

/// <summary>
/// The opt-in test of a 4-zone keyboard on a model where it is likely to work but RaptorSense hasn't been tried
/// (<see cref="LaptopModels.KeyboardCandidates"/>). It only ever changes the keyboard's colours. Once an owner has seen
/// the colours come out right, the app remembers the order of calls that worked, for that model alone.
/// </summary>
public static class FourZoneTrial
{
    /// <summary>Red, green, blue and white, left to right: a swapped channel or zone shows at a glance.</summary>
    public static readonly IReadOnlyList<Rgb> TestColors = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), Rgb.White];

    /// <summary>The change the test sends. Nothing is read back to confirm it: the owner's eyes decide.</summary>
    public static FourZoneRequest Test(FourZoneOrder order) => new()
    {
        Colors = TestColors.Select(c => c.ToString()).ToList(), Trial = true, Order = order, Confirm = false, Describe = true,
    };

    /// <summary>The keyboard's own wave, to leave it lit after a test that didn't work.</summary>
    public static FourZoneRequest PutBack() => new()
    {
        Colors = [Rgb.White.ToString()], Mode = FourZoneMode.Wave, Trial = true, Confirm = false,
    };

    /// <summary>How an accepted test is saved: the model and the order that worked.</summary>
    public static string Save(string model, FourZoneOrder order) => $"{model.Trim()}|{order}";

    /// <summary>
    /// The order an owner accepted on this model, or null. A saved answer counts only on the model it was given on, and
    /// only while that model may be tried.
    /// </summary>
    public static FourZoneOrder? Load(string? saved, string? model)
    {
        if (saved is null || model is null || !LaptopModels.MayTryFourZoneKeyboard(model)) return null;
        int bar = saved.LastIndexOf('|');
        return bar > 0 && saved[..bar].Equals(model.Trim(), StringComparison.OrdinalIgnoreCase) && char.IsLetter(saved[^1]) &&
               Enum.TryParse<FourZoneOrder>(saved[(bar + 1)..], out var order) && Enum.IsDefined(order)
            ? order : null;
    }

    /// <summary>
    /// What to paste into a laptop report. English, like the form, and nothing in it identifies the laptop's owner: the
    /// model, the version, what was seen and what the firmware answered.
    /// </summary>
    public static string Report(string? model, string version, IEnumerable<(FourZoneOrder Order, string Outcome, string? Detail)> attempts)
    {
        var lines = new List<string> { $"4-zone keyboard test, RaptorSense {version}", $"Laptop: {LaptopModels.Describe(model)}" };
        foreach (var (order, outcome, detail) in attempts)
        {
            lines.Add($"{order}: {outcome}");
            if (!string.IsNullOrWhiteSpace(detail)) lines.Add($"  {detail}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The owner's answer, for the report.</summary>
    public static string Describe(FourZoneSeen seen) => seen switch
    {
        FourZoneSeen.AllRight => "red, green, blue, white from left to right",
        FourZoneSeen.MixedUp => "lit, but the colours were in other places or wrong",
        _ => "nothing changed, or the keys went dark",
    };
}
