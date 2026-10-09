using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class WirelessDeviceAliasTests
{
    private const string _devices = """
        List of devices attached
        192.168.1.20:30001 device product:test model:Same_Model device:example
        adb-phone._adb-tls-connect._tcp device product:test model:Same_Model device:example
        usb-example device usb:1 product:test model:Same_Model device:example
        """;
    private const string _mdns = "adb-phone _adb-tls-connect._tcp. 192.168.1.20:30001";

    [Theory]
    [InlineData(null, "adb-phone._adb-tls-connect._tcp")]
    [InlineData("192.168.1.20:30001", "192.168.1.20:30001")]
    [InlineData("adb-phone._adb-tls-connect._tcp", "adb-phone._adb-tls-connect._tcp")]
    public void DuplicateWirelessNamesCollapseWhileUsbAndActiveTransportRemain(string? preferred, string expected)
    {
        var devices = AdbOutputParser.ParseDevices(_devices);
        Assert.Equal(3, devices.Count); // Previously three rows, including the duplicate network endpoint.
        Assert.True(WirelessAdbDeviceAliases.NeedsResolution(devices));
        var collapsed = WirelessAdbDeviceAliases.Collapse(devices, _mdns,
            preferred is null ? null : AdbOutputParser.CreateDeviceKey(preferred));
        Assert.Equal(2, collapsed.Count);
        Assert.Contains(collapsed, device => device.Transport == AndroidTransport.Usb);
        Assert.Equal(expected, Assert.Single(collapsed, device => device.Transport == AndroidTransport.Network).Serial);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("adb-phone _adb-tls-connect._tcp 192.168.1.20:30002")]
    [InlineData("adb-phone _adb-tls-pairing._tcp 192.168.1.20:30001")]
    [InlineData("another-phone _adb-tls-connect._tcp 192.168.1.20:30001")]
    public void MissingOrDifferentEndpointEvidenceNeverMergesDevices(string? mdns)
    {
        Assert.Equal(3, WirelessAdbDeviceAliases.Collapse(AdbOutputParser.ParseDevices(_devices), mdns, null).Count);
    }

    [Fact]
    public void OfflinePreferredAliasDoesNotHideItsReadyTransport()
    {
        var devices = AdbOutputParser.ParseDevices(_devices.Replace("192.168.1.20:30001 device", "192.168.1.20:30001 offline", StringComparison.Ordinal));
        var collapsed = WirelessAdbDeviceAliases.Collapse(devices, _mdns, AdbOutputParser.CreateDeviceKey("192.168.1.20:30001"));
        Assert.Equal(AndroidDeviceStatus.Ready, Assert.Single(collapsed, device => device.Transport == AndroidTransport.Network).Status);
    }

    [Fact]
    public void UsbOnlyAndNamedWirelessOnlyDoNotRequireAnotherDiscoveryCommand()
    {
        var devices = AdbOutputParser.ParseDevices(_devices);
        Assert.False(WirelessAdbDeviceAliases.NeedsResolution(devices.Where(device => device.Transport == AndroidTransport.Usb).ToArray()));
        Assert.False(WirelessAdbDeviceAliases.NeedsResolution(devices.Where(device => !device.Serial.Contains(':')).ToArray()));
    }
}
