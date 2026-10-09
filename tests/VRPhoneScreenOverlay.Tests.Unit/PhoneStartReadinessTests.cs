using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneStartReadinessTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void StartButtonRequiresAdbAuthorizedPhoneAndSteamVr(bool adb, bool phone, bool steamVr)
    {
        AndroidConnectionSnapshot connection = new(1, adb ? AndroidConnectionState.Ready : AndroidConnectionState.Faulted,
            "TEST", "test", phone ? [new("device", "test", "test", AndroidDeviceStatus.Ready, AndroidTransport.Usb, true)] : [],
            phone ? new("device", "test", "test", "test", "test", "16", 36, "arm64-v8a", AndroidTransport.Usb) : null,
            DateTimeOffset.UtcNow, null);
        bool ready = PhoneStartReadiness.CanStart(connection, steamVr, "device");
        Assert.Equal(adb && phone && steamVr, ready);
        using ModernButton button = new();
        PhoneOverlayButtonPolicy.Apply(button, false, ready, false);
        Assert.Equal(ready, button.Enabled);
        Assert.Equal(ready ? UiButtonTone.Success : UiButtonTone.Neutral, button.Tone);
    }

    [Fact]
    public void LosingConnectionsNeverDisablesTheStopButtonForAnOpenOverlay()
    {
        using ModernButton button = new();
        PhoneOverlayButtonPolicy.Apply(button, true, false, false);
        Assert.True(button.Enabled);
        Assert.Equal(UiButtonTone.Danger, button.Tone);
        PhoneOverlayButtonPolicy.Apply(button, true, false, true);
        Assert.False(button.Enabled);
    }
}
