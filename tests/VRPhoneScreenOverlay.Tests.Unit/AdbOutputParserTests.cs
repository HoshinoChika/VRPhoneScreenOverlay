using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AdbOutputParserTests
{
    [Fact]
    public void ParsesUsbWirelessUnauthorizedAndOfflineDevices()
    {
        const string output = """
            * daemon started successfully
            List of devices attached
            eb35981e device usb:1-2 product:fuxi model:2211133C device:fuxi transport_id:1
            192.168.1.20:5555 device product:honor model:HONOR_500_Pro device:honor transport_id:2
            pending-device unauthorized usb:3-1 transport_id:3
            192.168.1.30:5555 offline transport_id:4

            """;

        IReadOnlyList<AdbDeviceRecord> devices = AdbOutputParser.ParseDevices(output);

        Assert.Equal(4, devices.Count);
        Assert.Equal(AndroidTransport.Usb, devices[0].Transport);
        Assert.Equal("2211133C", devices[0].Model);
        Assert.Equal(AndroidTransport.Network, devices[1].Transport);
        Assert.Equal("HONOR 500 Pro", devices[1].Model);
        Assert.Equal(AndroidDeviceStatus.Unauthorized, devices[2].Status);
        Assert.Equal(AndroidDeviceStatus.Offline, devices[3].Status);
        Assert.All(devices, device => Assert.Equal(12, device.DeviceKey.Length));
    }

    [Fact]
    public void ParsesNoPermissionsStateWithoutTreatingPermissionsAsProperties()
    {
        const string output = """
            List of devices attached
            blocked no permissions (user in plugdev group; are your udev rules wrong?)

            """;

        AdbDeviceRecord device = Assert.Single(AdbOutputParser.ParseDevices(output));

        Assert.Equal(AndroidDeviceStatus.NoPermissions, device.Status);
    }

    [Fact]
    public void ParsesGetPropertyOutputWithWindowsLineEndings()
    {
        const string output = "[ro.product.manufacturer]: [Xiaomi]\r\n" +
                              "[ro.product.model]: [2211133C]\r\n" +
                              "[ro.build.version.sdk]: [35]\r\n";

        IReadOnlyDictionary<string, string> properties = AdbOutputParser.ParseProperties(output);

        Assert.Equal("Xiaomi", properties["ro.product.manufacturer"]);
        Assert.Equal("2211133C", properties["ro.product.model"]);
        Assert.Equal("35", properties["ro.build.version.sdk"]);
    }

    [Fact]
    public void DeviceSelectorPrefersUsbByDefaultAndHonorsExplicitNetworkSelection()
    {
        IReadOnlyList<AdbDeviceRecord> devices = AdbOutputParser.ParseDevices("""
            List of devices attached
            192.168.1.20:5555 device model:Network_Phone
            usb-device device usb:1-1 model:Usb_Phone

            """);

        AdbDeviceRecord automatic = Assert.IsType<AdbDeviceRecord>(
            AndroidDeviceSelector.Select(devices, null));
        AdbDeviceRecord preferred = Assert.IsType<AdbDeviceRecord>(
            AndroidDeviceSelector.Select(devices, devices[0].DeviceKey));

        Assert.Equal("Usb Phone", automatic.Model);
        Assert.Equal("Network Phone", preferred.Model);
        Assert.Equal(devices[0], AndroidDeviceSelector.Select([devices[0]], devices[0].DeviceKey));
    }
}
