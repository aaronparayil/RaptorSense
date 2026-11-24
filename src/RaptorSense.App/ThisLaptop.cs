using RaptorSense.Hardware;
using RaptorSense.Layout;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>
/// Which laptop the app is running on, read once at start from the model Windows reports (no administrator rights
/// needed). The window shows only what works on it: one app for every laptop, each seeing its own controls.
/// </summary>
static class ThisLaptop
{
    public static string? Model { get; } = LaptopModels.ReadThisModel();

    /// <summary>A 4-zone keyboard on an ENE controller, or null.</summary>
    public static EneKeyboard? EneKeyboard { get; } = Hardware.EneKeyboard.TryOpen(Model);

    /// <summary>The order of firmware calls this laptop's owner saw light its 4-zone keyboard, if they tried it.</summary>
    public static FourZoneOrder? TriedOrder { get; } =
        OperatingSystem.IsWindows() && EneKeyboard is null ? FourZoneTrial.Load(AppearanceSettings.Get(TriedKey), Model) : null;

    /// <summary>
    /// The owner may try the keyboard's colours: a model where that is likely to work, with no other keyboard that
    /// RaptorSense can already light.
    /// </summary>
    public static bool CanTryFourZones { get; } =
        OperatingSystem.IsWindows() &&   // it goes through the helper
        TriedOrder is null && EneKeyboard is null && LaptopModels.MayTryFourZoneKeyboard(Model) && !HasPerKeyKeyboard();

    /// <summary>The keyboard drawn and driven: four zones, or the Helios's per-key keyboard (also on unknown models).</summary>
    public static LaptopLayout Layout { get; } = LaptopLayouts.For(Model, EneKeyboard is not null || TriedOrder is not null);

    /// <summary>A keyboard that lights in four zones.</summary>
    public static bool HasFourZoneKeyboard => Layout.HasZones();

    /// <summary>Zones in the firmware are lit through the helper; every other keyboard is reached without it.</summary>
    public static bool KeyboardNeedsHelper => Layout == LaptopLayout.Nitro || TriedOrder is not null || CanTryFourZones;

    /// <summary>The Helios's rear light bar. Not on the Nitro.</summary>
    public static bool HasLightBar => !HasFourZoneKeyboard;

    /// <summary>
    /// The fans can be changed: only on models checked for it (<see cref="LaptopModels.HasFanControl"/>: the Helios
    /// and the Nitro). Elsewhere the Performance tab shows the readings and leaves out the controls.
    /// </summary>
    public static bool HasFanControl => LaptopModels.HasFanControl(Model);

    /// <summary>Power modes can be changed: only on fully checked models (<see cref="LaptopModels.Verified"/>, the Helios).</summary>
    public static bool HasPowerControl => LaptopModels.IsVerified(Model);

    const string TriedKey = "FourZoneKeyboard";

    /// <summary>Remembers the answer to the test, for this model; null forgets it. Counts from the next start.</summary>
    public static void RememberTried(FourZoneOrder? order) =>
        AppearanceSettings.Set(TriedKey, order is { } o && Model is { } model ? FourZoneTrial.Save(model, o) : null);

    static bool HasPerKeyKeyboard()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var keyboard = Ph16Keyboard.TryOpen();
        return keyboard is not null;
    }
}
