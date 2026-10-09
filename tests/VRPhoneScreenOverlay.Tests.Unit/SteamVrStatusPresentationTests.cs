using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class SteamVrStatusPresentationTests
{
    [Fact]
    public void TimeoutRemainsVisibleDuringWaitingButDoesNotHideRecovery()
    {
        PhoneOverlaySnapshot overlay = new(PhoneOverlayState.Stopped, "STOPPED", "test");
        OpenVrBindingResult timeout = new(false, OpenVrReasonCodes.StartupRegistrationTimeout, "SteamVR 连接超时");
        for (int refresh = 0; refresh < 3; refresh++)
        { Assert.Equal("连接超时", SteamVrStatusPresentation.Text(overlay, OpenVrPlayspaceDragState.WaitingForSteamVr, timeout)); }
        Assert.Equal("已连接", SteamVrStatusPresentation.Text(overlay, OpenVrPlayspaceDragState.Ready, timeout));
    }

    [Fact]
    public void ConnectionNoticeKeepsItsActualReasonAndDoesNotSuggestBindingReset()
    {
        using SteamVrBindingNotice notice = new();
        notice.UpdateConnectionNotice("SteamVR 连接超时，请检查运行权限");
        Assert.Contains(notice.Controls.OfType<System.Windows.Forms.Label>(), label => label.Text.Contains("请检查运行权限", StringComparison.Ordinal));
        Assert.False(notice.Controls.OfType<ModernButton>().Single().Visible);
    }

    [Theory]
    [InlineData(OpenVrPlayspaceDragState.Ready, "已连接")]
    [InlineData(OpenVrPlayspaceDragState.WaitingForSteamVr, "等待启动")]
    [InlineData(OpenVrPlayspaceDragState.Faulted, "连接异常")]
    public void LostPhoneVideoDoesNotInventARuntimeFailure(OpenVrPlayspaceDragState runtime, string expected) =>
        Assert.Equal(expected, SteamVrStatusPresentation.Text(new(PhoneOverlayState.Faulted, "VIDEO_LOST", "test"), runtime));
}
