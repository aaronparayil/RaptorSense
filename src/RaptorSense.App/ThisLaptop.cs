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

    /// <summary>The keyboard drawn and driven: the Nitro's zones, or the Helios's per-key keyboard (also on unknown models).</summary>
    public static LaptopLayout Layout { get; } = LaptopLayouts.For(Model);

    /// <summary>A Nitro 5 AN515-57's 4-zone keyboard, lit through the helper.</summary>
    public static bool HasFourZoneKeyboard => Layout == LaptopLayout.Nitro;

    /// <summary>The Helios's rear light bar. Not on the Nitro.</summary>
    public static bool HasLightBar => !HasFourZoneKeyboard;

    /// <summary>
    /// The fans can be changed: only on models checked for it (<see cref="LaptopModels.HasFanControl"/>: the Helios
    /// and the Nitro). Elsewhere the Performance tab shows the readings and leaves out the controls.
    /// </summary>
    public static bool HasFanControl => LaptopModels.HasFanControl(Model);

    /// <summary>Power modes can be changed: only on fully checked models (<see cref="LaptopModels.Verified"/>, the Helios).</summary>
    public static bool HasPowerControl => LaptopModels.IsVerified(Model);
}
