using RaptorSense.Hardware;
using Xunit;

namespace RaptorSense.Tests;

public sealed class LinuxHidTests
{
    // A vendor interface shaped like the keyboard's lighting one: usage page FF02, 64-byte output reports and 8-byte
    // feature reports, no report ids. (Written by hand; the real descriptor has not been read yet.)
    static readonly byte[] Lighting =
    [
        0x06, 0x02, 0xFF,   // Usage Page (FF02)
        0x09, 0x01,         // Usage (1)
        0xA1, 0x01,         // Collection (Application)
        0x09, 0x02, 0x15, 0x00, 0x26, 0xFF, 0x00,
        0x75, 0x08, 0x95, 0x40, 0x91, 0x02,   // 64 bytes out
        0x09, 0x03, 0x95, 0x08, 0xB1, 0x02,   // 8 bytes feature
        0xC0,
    ];

    // Two collections sharing an interface, told apart by report id: consumer keys (id 1) and usage page 88 (id 4).
    static readonly byte[] Shared =
    [
        0x05, 0x0C, 0x09, 0x01, 0xA1, 0x01, 0x85, 0x01, 0x75, 0x10, 0x95, 0x01, 0x81, 0x00, 0xC0,
        0x05, 0x88, 0x09, 0x01, 0xA1, 0x01, 0x85, 0x04, 0x75, 0x08, 0x95, 0x02, 0x81, 0x00, 0xC0,
    ];

    static readonly byte[] PlainKeyboard = [0x05, 0x01, 0x09, 0x06, 0xA1, 0x01, 0x75, 0x08, 0x95, 0x08, 0x81, 0x00, 0xC0];

    [Fact]
    public void A_vendor_interface_gives_its_usage_page_and_report_lengths()
    {
        var d = HidReportDescriptor.Parse(Lighting);
        Assert.Equal([new HidCollection(0xFF02, 1)], d.Collections);
        Assert.False(d.UsesReportIds);
        Assert.Equal(64, d.OutputBytes);
        Assert.Equal(8, d.FeatureBytes);
        Assert.Equal(0, d.InputBytes);
        Assert.True(d.Has(0xFF02));
        Assert.False(d.Has(0x0088));
    }

    [Fact]
    public void Collections_sharing_an_interface_are_all_listed()
    {
        var d = HidReportDescriptor.Parse(Shared);
        Assert.Equal([new HidCollection(0x000C, 1), new HidCollection(0x0088, 1)], d.Collections);
        Assert.True(d.UsesReportIds);
        Assert.Equal(2, d.InputBytes);
    }

    [Fact]
    public void A_nested_collection_is_not_a_device_of_its_own()
    {
        byte[] nested = [0x05, 0x01, 0x09, 0x02, 0xA1, 0x01, 0x09, 0x01, 0xA1, 0x00, 0x75, 0x08, 0x95, 0x03, 0x81, 0x02, 0xC0, 0xC0];
        Assert.Equal([new HidCollection(1, 2)], HidReportDescriptor.Parse(nested).Collections);
    }

    [Fact]
    public void A_four_byte_usage_carries_its_page()
    {
        byte[] extended = [0x0B, 0x01, 0x00, 0x02, 0xFF, 0xA1, 0x01, 0xC0];
        Assert.Equal([new HidCollection(0xFF02, 1)], HidReportDescriptor.Parse(extended).Collections);
    }

    [Fact]
    public void A_cut_off_or_empty_descriptor_does_not_throw()
    {
        for (int length = 0; length <= Lighting.Length; length++) HidReportDescriptor.Parse(Lighting.AsSpan(0, length));
        Assert.Empty(HidReportDescriptor.Parse([]).Collections);
        Assert.Empty(HidReportDescriptor.Parse([0xFE]).Collections);            // a long item with nothing after it
        Assert.Empty(HidReportDescriptor.Parse([0xFE, 0x40, 0x01]).Collections);   // one that claims more than is there
    }

    [Fact]
    public void The_feature_ioctl_numbers_match_the_kernel_header()
    {
        Assert.Equal(0xC0094806u, HidrawDevice.FeatureRequest(6, 9));   // HIDIOCSFEATURE(9)
        Assert.Equal(0xC0094807u, HidrawDevice.FeatureRequest(7, 9));   // HIDIOCGFEATURE(9)
    }

    [Fact]
    public void Only_the_interface_with_the_wanted_usage_page_is_found()
    {
        var root = Path.Combine(Path.GetTempPath(), "raptorsense-hidraw-" + Guid.NewGuid().ToString("N"));
        try
        {
            AddDevice(root, "hidraw0", "HID_ID=0003:0000046D:0000C52B", Lighting);       // another maker's device
            AddDevice(root, "hidraw1", "HID_ID=0003:000004F2:00000117", PlainKeyboard);  // the keyboard's typing interface
            AddDevice(root, "hidraw2", "HID_ID=0003:000004F2:00000117", Shared);
            AddDevice(root, "hidraw3", "HID_ID=0003:000004F2:00000117", Lighting);
            Directory.CreateDirectory(Path.Combine(root, "hidraw4", "device"));           // one that vanished half-way

            ushort[] pids = [0x0117, 0x011A, 0x0119];
            var lighting = Assert.Single(HidrawDevice.Find(root, 0x04F2, pids, 0xFF02));
            Assert.Equal("hidraw3", lighting.Name);
            Assert.Equal("/dev/hidraw3", lighting.DevicePath);
            Assert.Equal((ushort)0x0117, lighting.ProductId);
            Assert.Equal(64, lighting.Descriptor.OutputBytes);

            Assert.Equal("hidraw2", Assert.Single(HidrawDevice.Find(root, 0x04F2, pids, 0x0088)).Name);
            Assert.Empty(HidrawDevice.Find(root, 0x04F2, [0x011A], 0xFF02));
            Assert.Empty(HidrawDevice.Find(Path.Combine(root, "missing"), 0x04F2, pids, 0xFF02));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    static void AddDevice(string root, string name, string idLine, byte[] descriptor)
    {
        var device = Path.Combine(root, name, "device");
        Directory.CreateDirectory(device);
        File.WriteAllText(Path.Combine(device, "uevent"), $"DRIVER=hid-generic\n{idLine}\nHID_NAME=Test\n");
        File.WriteAllBytes(Path.Combine(device, "report_descriptor"), descriptor);
    }
}
