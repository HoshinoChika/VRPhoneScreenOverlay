using System.Windows.Forms;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class MainPageNavigationTests
{
    private static readonly string[] _aboutCards = ["updateCard", "diagnosticsCard", "productCard"];
    private static readonly string[] _homeCards = ["deviceCard", "metricsCard", "playspaceCard"];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturningHomeOrSelectingHomeAgainClosesConnectionEditorAndDiscardsOnlyItsDraft(bool visitAbout)
    {
        RunOnUiThread(() =>
        {
            using MainShellView shell = new();
            using HomePageView home = new();
            using AboutPageView about = new();
            shell.PageHost.Controls.Add(home);
            shell.PageHost.Controls.Add(about);
            shell.ShowMainPage(home, shell.HomeNavigation);
            home.ShowWireless();
            home.WirelessPanel.SelectMethod(WirelessPairingMethod.Code);
            home.WirelessPanel.ShowDeviceManager(new("sample", "已保存平板", "DEMO", AndroidTransport.Network,
                true, AndroidDeviceStatus.Ready, true, true, DateTimeOffset.UnixEpoch, "已保存平板"));
            home.WirelessPanel.DeviceManager.RenameTextBox.Text = "未保存名称";
            home.WirelessPanel.DeviceManager.AutoConnectToggle.Checked = false;
            home.PhoneName.Text = "当前连接平板";
            home.VideoStatus.Text = "运行中";
            home.InertiaToggle.Checked = true;
            if (visitAbout) { shell.ShowMainPage(about, shell.AboutNavigation); }
            int visibleQrRestarts = 0;
            home.WirelessPanel.VisibleChanged += (_, _) => { if (home.WirelessPanel.Visible) { visibleQrRestarts++; } };
            shell.ShowMainPage(home, shell.HomeNavigation);
            Assert.True(home.Visible);
            Assert.False(about.Visible);
            Assert.False(home.WirelessPanel.Visible);
            Assert.False(home.MotionPanel.Visible);
            Assert.False(home.WirelessPanel.DeviceManagerHost.Visible);
            Assert.Equal(0, visibleQrRestarts);
            Assert.Equal("已保存平板", home.WirelessPanel.DeviceManager.RenameTextBox.Text);
            Assert.True(home.WirelessPanel.DeviceManager.AutoConnectToggle.Checked);
            Assert.Equal(WirelessPairingMethod.Code, home.WirelessPanel.Method);
            Assert.Equal("当前连接平板", home.PhoneName.Text);
            Assert.Equal("运行中", home.VideoStatus.Text);
            Assert.True(home.InertiaToggle.Checked);
            foreach (string card in _homeCards)
            { Assert.True(home.Controls.Find(card, true).Single().Visible); }
            home.ShowMotion();
            shell.ShowMainPage(home, shell.HomeNavigation);
            Assert.False(home.MotionPanel.Visible);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AboutNavigationReturnsToMainContentAndKeepsDiagnosticText(bool leaveFirst)
    {
        RunOnUiThread(() =>
        {
            using MainShellView shell = new();
            using AboutPageView about = new();
            using HomePageView home = new();
            shell.PageHost.Controls.Add(about);
            shell.PageHost.Controls.Add(home);
            shell.ShowMainPage(about, shell.AboutNavigation);
            about.ShowDiagnosticReport();
            foreach (string card in _aboutCards)
            { Assert.False(about.Controls.Find(card, true).Single().Visible); }
            about.IssueDescriptionTextBox.Text = "待提交的问题描述";
            about.Controls.Find("enlargedCodePanel", true).Single().Visible = true;
            if (leaveFirst) { shell.ShowMainPage(home, shell.HomeNavigation); }
            shell.ShowMainPage(about, shell.AboutNavigation);
            Assert.True(about.Visible);
            Assert.False(about.DiagnosticReportPanel.Visible);
            foreach (string card in _aboutCards)
            { Assert.True(about.Controls.Find(card, true).Single().Visible); }
            Assert.False(about.Controls.Find("enlargedCodePanel", true).Single().Visible);
            Assert.Equal("待提交的问题描述", about.IssueDescriptionTextBox.Text);
        });
    }

    [Fact]
    public void EverySidebarDestinationSelectsOnlyItsMainPageAndDismissesHelp()
    {
        RunOnUiThread(() =>
        {
            using MainShellView shell = new();
            using HomePageView home = new();
            using ControllerBindingsView features = new();
            using VideoPageView video = new();
            using SettingsPageView settings = new();
            using AboutPageView about = new();
            (Control Page, NavigationButton Button)[] destinations =
                [(home, shell.HomeNavigation), (features, shell.FeaturesNavigation), (video, shell.VideoNavigation),
                (settings, shell.SettingsNavigation), (about, shell.AboutNavigation)];
            foreach (var destination in destinations) { shell.PageHost.Controls.Add(destination.Page); }
            shell.CreateControl();
            foreach (var destination in destinations)
            {
                shell.ShowMainPage(settings, shell.SettingsNavigation);
                ((ContextHelpIcon)settings.Controls.Find("autoOpenLabelHelp", true).Single()).ShowHelp();
                Assert.True(shell.ContextHelp.Visible);
                shell.ShowMainPage(destination.Page, destination.Button);
                Assert.False(shell.ContextHelp.Visible);
                foreach (var candidate in destinations)
                {
                    Assert.Equal(candidate.Page == destination.Page, candidate.Page.Visible);
                    Assert.Equal(candidate.Button == destination.Button, candidate.Button.Selected);
                }
            }
        });
    }

    private static void RunOnUiThread(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() => { try { test(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
