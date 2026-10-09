using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class BindingRecoveryNoticeTests
{
    [Fact]
    public void AssembledFeaturesPageExposesRecoveryOnlyWhenBindingFails()
    {
        using ControllerBindingsView page = new();
        SteamVrBindingNotice notice = page.BindingNotice;
        Assert.False(notice.Visible);
        notice.UpdateNotice(OpenVrBindingHealthState.Failed, true, "Synthetic failure");
        Assert.True(notice.Visible);
        ModernButton reload = Assert.Single(notice.Controls.OfType<ModernButton>());
        Assert.True(reload.Visible);
        Assert.True(reload.Enabled);
        Assert.Equal("重新加载本地绑定", reload.Text);
        notice.UpdateNotice(OpenVrBindingHealthState.Ready, false, "");
        Assert.False(notice.Visible);
    }
}
