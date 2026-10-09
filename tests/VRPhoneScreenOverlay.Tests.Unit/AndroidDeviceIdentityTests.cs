using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.App;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidDeviceIdentityTests
{
    [Fact]
    public void FirmwareMarketingNameWinsWithoutGuessingAnIdMapping()
    {
        Dictionary<string, string> properties = new()
        {
            ["ro.product.marketname"] = "示例手机 Pro",
            ["ro.product.vendor.marketname"] = "Fallback",
        };
        Assert.Equal("示例手机 Pro", AndroidDeviceIdentity.ReadMarketName(properties));
        AdbProbeResult probe = new(true, "", "", "Example", "Example", "MODEL-01", "device", "15", 35, "arm64", 1080, 2400, TimeSpan.Zero);
        Assert.Equal("示例手机 Pro", AndroidDeviceIdentity.DisplayName(probe with { MarketName = "示例手机 Pro" }));
        Assert.Equal("ID MODEL-01", AndroidDeviceIdentity.DisplayName(probe));
        Assert.Equal("", AndroidDeviceIdentity.ReadMarketName(new Dictionary<string, string>()));
    }

    [Fact]
    public void ListDistinguishesConnectionAndSelectionWithoutExposingPrivateDeviceKey()
    {
        AndroidDeviceView device = new("internal-private-key", "示例手机 Pro", "MODEL-01", AndroidDeviceStatus.Ready,
            AndroidTransport.Network, true, "15");
        Assert.Equal("示例手机 Pro　|　无线 · 当前　|　ID MODEL-01　|　Android 15　|　已授权", DeviceListText.Format(device));
        string pending = DeviceListText.Format(device with { Status = AndroidDeviceStatus.Unauthorized, AndroidVersion = "" });
        Assert.Contains("Android 未知", pending, StringComparison.Ordinal);
        Assert.Contains("等待手机授权", pending, StringComparison.Ordinal);
        Assert.DoesNotContain(device.DeviceKey, pending, StringComparison.Ordinal);
        Assert.Contains("无线", pending, StringComparison.Ordinal);
        string usb = DeviceListText.Format(device with { Transport = AndroidTransport.Usb, IsSelected = false });
        Assert.Contains("USB", usb, StringComparison.Ordinal);
        Assert.DoesNotContain("当前", usb, StringComparison.Ordinal);
    }
}
