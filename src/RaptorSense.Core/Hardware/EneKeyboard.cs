using System.ComponentModel;
using RaptorSense.Lighting;
using RaptorSense.Thermal;

namespace RaptorSense.Hardware;

/// <summary>Feature reports of the ENE lighting controller. The report id is the first byte of each report.</summary>
public interface IEneReports : IDisposable
{
    void Set(ReadOnlySpan<byte> report);

    /// <summary>Reads a report of up to <paramref name="length"/> bytes, the report id included.</summary>
    byte[] Get(byte reportId, int length);
}

/// <summary>What the controller says about one of the lights it drives (report A3).</summary>
public readonly record struct EneCapabilities(byte Target, int Zones, uint Modes)
{
    /// <summary>The controller lists its modes as bits: bit 0 is mode 1.</summary>
    public bool Supports(byte mode) => mode is > 0 and <= 32 && (Modes & (1u << (mode - 1))) != 0;
}

/// <summary>
/// Byte layouts of the ENE KB5130 lighting controller (ENEK5130, I2C HID 0CF2:5130) that lights the 4-zone keyboards of
/// some of Acer's 2025 laptops, such as the Helios Neo 16S PHN16S-71. RaptorSense has not measured these: the layouts
/// are from the notes of kbrgb, predator-sense and PreySense (docs/protocol.md, THIRD_PARTY_NOTICES.md).
/// </summary>
public static class EneProtocol
{
    public const ushort VendorId = 0x0CF2, ProductId = 0x5130;

    public const byte TargetListReport = 0xA1, SelectReport = 0xA2, CapabilitiesReport = 0xA3, LightingReport = 0xA4;
    public const int TargetListLength = 11, CapabilitiesLength = 9, LightingLength = 11;

    public const byte Keyboard = 0x21;
    public const byte AllZones = 0x0F;
    public const byte StaticMode = 0x02;

    /// <summary>The lights in an A1 report (<c>A1 count target...</c>), or none if it isn't one.</summary>
    public static byte[] ParseTargets(ReadOnlySpan<byte> report)
    {
        if (report.Length < 2 || report[0] != TargetListReport || report[1] > report.Length - 2) return [];
        return report.Slice(2, report[1]).ToArray();
    }

    /// <summary>The report that picks which light the next A3 report describes. It changes no lighting.</summary>
    public static byte[] Select(byte target) => [SelectReport, target];

    /// <summary>An A3 report (<c>A3 target .. .. zones modes[4]</c>), or null if it isn't about <paramref name="target"/>.</summary>
    public static EneCapabilities? ParseCapabilities(byte target, ReadOnlySpan<byte> report)
    {
        if (report.Length < CapabilitiesLength || report[0] != CapabilitiesReport || report[1] != target || report[4] is 0 or > 16) return null;
        return new EneCapabilities(target, report[4], (uint)(report[5] | report[6] << 8 | report[7] << 16 | report[8] << 24));
    }

    /// <summary>One colour on the zones in <paramref name="zoneMask"/> (bit 0 = leftmost). Takes effect at once, with no fade.</summary>
    public static byte[] Static(byte zoneMask, Rgb color, int brightness) =>
        [LightingReport, Keyboard, StaticMode, Percent(brightness), 0, 0, color.R, color.G, color.B, zoneMask, 0];

    /// <summary>One of the controller's own effects, which it then plays by itself.</summary>
    /// <param name="plainFlag">Byte 5 for effects with no direction: see <see cref="PlainFlag"/>.</param>
    public static byte[] Effect(FourZoneMode mode, Rgb color, int speed, int brightness, FourZoneDirection direction, byte plainFlag)
    {
        byte flag = mode switch
        {
            FourZoneMode.Wave => direction == FourZoneDirection.LeftToRight ? (byte)1 : (byte)2,
            FourZoneMode.Breathing or FourZoneMode.Neon => plainFlag,
            _ => 0,
        };
        return
        [
            LightingReport, Keyboard, ModeCode(mode), Percent(brightness),
            (byte)Math.Clamp(speed, FourZoneProtocol.MinSpeed, FourZoneProtocol.MaxSpeed), flag, color.R, color.G, color.B, AllZones, 0,
        ];
    }

    /// <summary>
    /// The controller's number for a mode. Nothing else is ever sent: other numbers are known to switch the lights off
    /// (1, 3), freeze them (12) or glitch on some laptops (6, 8).
    /// </summary>
    public static byte ModeCode(FourZoneMode mode) => mode switch
    {
        FourZoneMode.Static => StaticMode,
        FourZoneMode.Breathing => 0x04,
        FourZoneMode.Neon => 0x05,
        FourZoneMode.Wave => 0x07,
        FourZoneMode.Shifting => 0x0A,   // a sweep across the zones
        FourZoneMode.Zoom => 0x09,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>Breathing and Neon were confirmed with 0 in byte 5 on the PHN16S-71, and with 2 on the AN16S-61.</summary>
    public static byte PlainFlag(string? model) =>
        model is not null && model.Split(' ', '/').Contains("PHN16S-71", StringComparer.OrdinalIgnoreCase) ? (byte)0 : (byte)2;

    static byte Percent(int brightness) => (byte)Math.Clamp(brightness, 0, FourZoneProtocol.MaxBrightness);
}

/// <summary>
/// A 4-zone keyboard behind an ENE KB5130 controller. It needs no administrator rights and no helper. Whether to drive
/// it is decided by what the controller answers, not by the laptop's model: it must list a keyboard, with four zones,
/// that takes static colours. Until it has, nothing that changes the lighting is sent.
/// </summary>
public sealed class EneKeyboard : IFourZoneKeyboard, IDisposable
{
    readonly Func<IEneReports?> open;
    readonly byte plainFlag;
    readonly Lock gate = new();
    IEneReports? reports;
    bool disposed;

    EneKeyboard(Func<IEneReports?> open, IEneReports reports, EneCapabilities capabilities, string? model)
    {
        this.open = open;
        this.reports = reports;
        Capabilities = capabilities;
        plainFlag = EneProtocol.PlainFlag(model);
    }

    public EneCapabilities Capabilities { get; }

    /// <summary>
    /// The keyboard, or null if this laptop has no such controller or it doesn't describe a 4-zone keyboard. Not looked
    /// for on the models known to light another way.
    /// </summary>
    /// <param name="model">The laptop's model: two of the effects need a different byte on one of them.</param>
    public static EneKeyboard? TryOpen(string? model) =>
        LaptopModels.IsVerified(model) || LaptopModels.HasFourZoneKeyboard(model) ? null
            : TryOpen(() => OperatingSystem.IsWindows() ? WindowsEneReports.Open() : null, model);

    public static EneKeyboard? TryOpen(Func<IEneReports?> open, string? model)
    {
        IEneReports? reports = null;
        try
        {
            reports = open();
            if (reports is not null && Identify(reports) is { } capabilities)
            {
                var keyboard = new EneKeyboard(open, reports, capabilities, model);
                reports = null;
                return keyboard;
            }
        }
        catch (Exception e) when (e is Win32Exception or IOException) { }   // it doesn't answer as expected: not ours to drive
        finally { reports?.Dispose(); }
        return null;
    }

    static EneCapabilities? Identify(IEneReports reports)
    {
        if (!EneProtocol.ParseTargets(reports.Get(EneProtocol.TargetListReport, EneProtocol.TargetListLength)).Contains(EneProtocol.Keyboard)) return null;
        reports.Set(EneProtocol.Select(EneProtocol.Keyboard));
        var found = EneProtocol.ParseCapabilities(EneProtocol.Keyboard, reports.Get(EneProtocol.CapabilitiesReport, EneProtocol.CapabilitiesLength));
        return found is { Zones: FourZoneProtocol.Zones } c && c.Supports(EneProtocol.StaticMode) ? c : null;
    }

    /// <summary>True when the controller says it plays this effect.</summary>
    public bool Supports(FourZoneMode mode) => Capabilities.Supports(EneProtocol.ModeCode(mode));

    public void SetZones(IReadOnlyList<Rgb> zones, int brightness, bool confirm = true, FourZoneOrder order = FourZoneOrder.ZonesFirst)
    {
        if (zones.Count != FourZoneProtocol.Zones) throw new ArgumentException("Four zone colours are needed.", nameof(zones));
        if (zones.All(z => z == zones[0]))
        {
            Send(EneProtocol.Static(EneProtocol.AllZones, zones[0], brightness));
            return;
        }
        Send([.. zones.Select((color, zone) => EneProtocol.Static((byte)(1 << zone), color, brightness))]);
    }

    public void SetEffect(FourZoneMode mode, Rgb color, int speed, int brightness, FourZoneDirection direction)
    {
        if (mode == FourZoneMode.Static)
        {
            SetZones([color, color, color, color], brightness);
            return;
        }
        if (!Supports(mode)) throw new NotSupportedException("This keyboard doesn't have that effect.");
        Send(EneProtocol.Effect(mode, color, speed, brightness, direction, plainFlag));
    }

    /// <summary>Sends the reports in order. After a failure (the laptop slept, say) the next send opens the controller again.</summary>
    void Send(params byte[][] all)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            reports ??= open() ?? throw new IOException("The keyboard's lighting controller isn't there.");
            try
            {
                foreach (var report in all) reports.Set(report);
            }
            catch
            {
                reports.Dispose();
                reports = null;
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            reports?.Dispose();
            reports = null;
        }
    }
}
