using System.ComponentModel;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>
/// Sends a change to this laptop's 4-zone keyboard: straight to its ENE controller where there is one, otherwise through
/// the helper (a Nitro 5 AN515-57, or a model whose owner tried it).
/// </summary>
static class ZoneLight
{
    /// <summary>Null when the keyboard took the change; otherwise what to tell the user.</summary>
    public static async Task<string?> SetAsync(FourZoneRequest request, CancellationToken cancel = default)
    {
        if (ThisLaptop.EneKeyboard is { } keyboard)
        {
            try
            {
                await Task.Run(() => request.ApplyTo(keyboard), cancel);
                return null;
            }
            catch (Exception e) when (e is Win32Exception or IOException or ObjectDisposedException or NotSupportedException or ArgumentException)
            {
                return L("The keyboard didn't take the change: {0}", e.Message);
            }
        }

        // Where it hasn't been checked, what the firmware reads back isn't known to be reliable: the owner saw it work.
        if (ThisLaptop.TriedOrder is { } order) request = request with { Trial = true, Order = order, Confirm = false };
        var status = await ThermalClient.SetKeyboardAsync(request, cancel);
        if (status.Ok) return null;
        return status.Reading is not null && status.HelperRevision < (request.Trial ? ThermalProtocol.TrialRevision : ThermalProtocol.FourZoneRevision)
            ? L("The helper installed is from an older RaptorSense that can't light this keyboard. Update it on the Performance tab.")
            : status.Error ?? L(ThermalClient.HelperMissing);
    }
}
