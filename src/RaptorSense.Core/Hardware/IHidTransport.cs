namespace RaptorSense.Hardware;

/// <summary>Raw access to one HID collection. Payloads exclude the report-id byte; transports add it.</summary>
public interface IHidTransport : IDisposable
{
    string Description { get; }

    void SetFeature(ReadOnlySpan<byte> payload);

    byte[] GetFeature();

    void WriteOutput(ReadOnlySpan<byte> payload);

    /// <summary>Serialises a group of transfers against other software that drives the same device.</summary>
    IDisposable Exclusive();
}
