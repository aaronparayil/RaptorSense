using RaptorSense.Hardware;
using RaptorSense.Lighting;
using Xunit;

namespace RaptorSense.Tests;

/// <summary>What looks black in the preview must be off on the keys, and full colours must stay full.</summary>
public sealed class LedCurveTests
{
    [Fact]
    public void EndsStayPut()
    {
        Assert.Equal(0, LedCurve.Apply((byte)0));
        Assert.Equal(255, LedCurve.Apply((byte)255));
    }

    [Fact]
    public void NeverGoesDownAsTheInputGoesUp()
    {
        for (int i = 1; i < 256; i++) Assert.True(LedCurve.Apply((byte)i) >= LedCurve.Apply((byte)(i - 1)), $"at {i}");
    }

    [Fact]
    public void DarkBackgroundsGoFullyOff()
    {
        // Radar's background with colour 2 at full pink, and the dimmest backgrounds effects use (up to 8% of a colour).
        Assert.Equal(Rgb.Black, LedCurve.Apply(new Rgb(10, 0, 5)));
        Assert.Equal(Rgb.Black, LedCurve.Apply(new Rgb(255, 255, 255).Scale(0.08)));
    }

    [Fact]
    public void MidTonesAreDimmedAsOnAScreen()
    {
        Assert.InRange(LedCurve.Apply((byte)128), 50, 60);
    }

    [Fact]
    public void FramesSentToTheKeyboardGoThroughTheCurve()
    {
        var transport = new RecordingTransport();
        using var keyboard = new Ph16Keyboard(transport);
        var frame = new Frame();
        frame.Fill(new Rgb(10, 128, 255));
        keyboard.SendFrame(frame, 50);

        byte[] sent = [.. transport.Output.SelectMany(packet => packet)];
        Assert.Equal(Frame.CellCount * 4, sent.Length);
        Assert.Equal(0, sent[1]);
        Assert.Equal(LedCurve.Apply((byte)128), sent[2]);
        Assert.Equal(255, sent[3]);
    }

    sealed class RecordingTransport : IHidTransport
    {
        public List<byte[]> Output { get; } = [];
        public string Description => "test";
        public void SetFeature(ReadOnlySpan<byte> payload) { }
        public byte[] GetFeature() => new byte[9];
        public void WriteOutput(ReadOnlySpan<byte> payload) => Output.Add(payload.ToArray());
        public IDisposable Exclusive() => new Nothing();
        public void Dispose() { }
        sealed class Nothing : IDisposable { public void Dispose() { } }
    }
}
