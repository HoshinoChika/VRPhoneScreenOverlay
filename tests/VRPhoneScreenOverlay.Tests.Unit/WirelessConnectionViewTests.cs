using System.Windows.Forms;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class WirelessConnectionViewTests
{
    [Fact]
    public void FirstInstallUsesUsbAndFourRoundChoicesAreMutuallyExclusive()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            Assert.Equal(WirelessPairingMethod.Usb, view.Method);
            var selector = Find<WirelessPairingMethodSelectorControl>(view, "methodSelector");
            Assert.Equal(4, selector.Controls.Count);
            Assert.All(selector.Controls.OfType<RadioButton>(), button => Assert.Equal(0, button.Top));
            Assert.True(selector.Height < 40);
            foreach (WirelessPairingMethod method in Enum.GetValues<WirelessPairingMethod>())
            {
                view.SelectMethod(method);
                Assert.Equal(method, view.Method);
                Assert.Single(selector.Controls.OfType<RadioButton>(), button => button.Checked);
            }
            Assert.False(view.CancelButton.Visible);
            Assert.Equal("刷新设备", view.RefreshButton.Text);
        });
    }

    [Fact]
    public void WaitingAndHistoryListsHaveEqualSpaceAndDifferentMembership()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            DateTimeOffset time = DateTimeOffset.UtcNow;
            view.SetDevices(
            [
                new("waiting", "等待手机", "demo", AndroidTransport.Usb, true, AndroidDeviceStatus.Unauthorized, false, false, null),
                new("remembered", "已知手机", "demo", AndroidTransport.Usb, true, AndroidDeviceStatus.Ready, false, true, time.AddDays(-1)),
                new("selected", "当前手机", "demo", AndroidTransport.Usb, true, AndroidDeviceStatus.Ready, true, true, time),
                new("offline", "历史手机", "demo", AndroidTransport.Network, false, AndroidDeviceStatus.Offline, false, false, time.AddDays(-2)),
            ]);
            Assert.Equal(["selected", "waiting", "remembered"], view.DeviceList.Items.Cast<ConnectionDeviceListEntry>().Select(entry => entry.Device.DeviceKey));
            Assert.Empty(view.HistoryDeviceList.Items);
            view.SelectMethod(WirelessPairingMethod.Scan);
            Assert.Empty(view.DeviceList.Items);
            Assert.Equal(["offline"], view.HistoryDeviceList.Items.Cast<ConnectionDeviceListEntry>().Select(entry => entry.Device.DeviceKey));
            Assert.Equal(view.DeviceList.Size, view.HistoryDeviceList.Size);
            Assert.True(view.DeviceList.Height >= 120);
            Assert.True(view.DeviceList.Parent!.Right < view.HistoryDeviceList.Parent!.Left);
        });
    }

    [Fact]
    public void DeviceHitTestingRejectsBlankSpaceWithAnExistingSelection()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            view.SetDevices([new("one", "示例手机", "demo", AndroidTransport.Usb, true, AndroidDeviceStatus.Ready, false, false, null)]);
            ConnectionDeviceList list = Assert.IsType<ConnectionDeviceList>(view.DeviceList);
            _ = list.Handle;
            list.SelectedIndex = 0;
            Assert.True(list.TryHitDevice(new(8, 8), out var hit));
            Assert.Equal("one", hit?.Device.DeviceKey);
            Assert.False(list.TryHitDevice(new(8, list.ClientSize.Height - 2), out _));
            Assert.False(WirelessConnectionView.ShouldOpenDevice(list, new(8, list.ClientSize.Height - 2)));
        });
    }

    [Fact]
    public void RecognizedHistoryMovesToCurrentAndForgettingUsesOriginalIdentity()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            var old = new ManagedAndroidDevice("old", "重命名手机", "ID1", AndroidTransport.Usb, false, AndroidDeviceStatus.Offline,
                false, true, DateTimeOffset.UtcNow, "重命名手机");
            view.SetDevices([old]);
            Assert.Empty(view.DeviceList.Items);
            Assert.Single(view.HistoryDeviceList.Items.Cast<object>());
            view.SetDevices([old with { IsAvailable = true, Status = AndroidDeviceStatus.Ready }]);
            Assert.Single(view.DeviceList.Items.Cast<object>());
            Assert.Empty(view.HistoryDeviceList.Items);
            view.SetDevices([old with { IsAvailable = true, Status = AndroidDeviceStatus.Ready, DisplayName = "XiaoMi13", LastConnectedAt = null, CustomName = null, AutoConnect = false }]);
            var fresh = Assert.IsType<ConnectionDeviceListEntry>(Assert.Single(view.DeviceList.Items.Cast<object>()));
            Assert.Equal("XiaoMi13", fresh.Device.DisplayName);
            Assert.Null(fresh.Device.LastConnectedAt);
        });
    }

    [Fact]
    public void CurrentModeSeparatesUsbFromWirelessAndOfflineHistoryDoesNotDuplicate()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            DateTimeOffset time = DateTimeOffset.UtcNow;
            view.SetDevices(
            [
                new("wifi", "无线手机", "demo", AndroidTransport.Network, true, AndroidDeviceStatus.Ready, true, true, time),
                new("usb", "USB手机", "demo", AndroidTransport.Usb, true, AndroidDeviceStatus.Ready, false, false, null),
                new("old", "旧手机", "demo", AndroidTransport.Network, true, AndroidDeviceStatus.Offline, false, true, time.AddDays(-1)),
            ]);
            Assert.Equal("usb", Assert.IsType<ConnectionDeviceListEntry>(Assert.Single(view.DeviceList.Items.Cast<object>())).Device.DeviceKey);
            Assert.Empty(view.HistoryDeviceList.Items);
            foreach (var method in new[] { WirelessPairingMethod.Scan, WirelessPairingMethod.Code, WirelessPairingMethod.Manual })
            {
                view.SelectMethod(method);
                Assert.Equal("wifi", Assert.IsType<ConnectionDeviceListEntry>(Assert.Single(view.DeviceList.Items.Cast<object>())).Device.DeviceKey);
            }
        });
    }

    [Fact]
    public void DialogContextControlsTheActionsAndCancelDiscardsUnsubmittedRename()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            ManagedAndroidDevice device = new("device", "原手机", "demo", AndroidTransport.Usb,
                true, AndroidDeviceStatus.Ready, false, true, DateTimeOffset.UtcNow, "原手机");
            view.ShowDeviceManager(device);
            Assert.True(view.DeviceManager.ConnectButton.Visible);
            Assert.True(view.DeviceManager.SaveButton.Right < view.DeviceManager.ConnectButton.Left);
            Assert.False(view.DeviceManager.ForgetButton.Visible);
            Assert.False(view.DeviceManager.CancelButton.CausesValidation);
            view.DeviceManager.RenameTextBox.Text = "未提交手机";
            view.DeviceManager.AutoConnectToggle.Checked = false;
            view.DeviceManager.CancelButton.PerformClick();
            Assert.False(view.DeviceManagerHost.Visible);
            Assert.False(view.DeviceManager.HasChanges);
            Assert.True(view.DeviceManager.AutoConnectToggle.Checked);
            view.ShowDeviceManager(device, history: true);
            Assert.False(view.DeviceManager.ConnectButton.Visible);
            Assert.True(view.DeviceManager.ForgetButton.Visible);
            Assert.True(view.DeviceManager.SaveButton.Right < view.DeviceManager.ForgetButton.Left);
            Assert.Equal("原手机", view.DeviceManager.RenameTextBox.Text);
        });
    }

    [Fact]
    public void PastedInvalidAndOverlongNamesAreRejectedAndOnlyValidNamesCanBeSubmitted()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            view.ShowDeviceManager(new("device", "示例手机", "demo", AndroidTransport.Usb, true, AndroidDeviceStatus.Ready,
                false, false, null, "原名"));
            TextBox name = view.DeviceManager.RenameTextBox;
            name.Text = "合法手机A1";
            name.Text = "带 空格😀";
            Assert.Equal("合法手机A1", name.Text);
            Assert.Contains("20", view.DeviceManager.Status.Text);
            name.Text = new string('中', 21);
            Assert.Equal("合法手机A1", name.Text);
            name.Text = new string('中', 20);
            Assert.True(view.DeviceManager.TryGetDraft(out DevicePreferenceDraft? submitted));
            Assert.Equal(new string('中', 20), submitted?.CustomName);
            name.Text = "";
            Assert.False(view.DeviceManager.TryGetDraft(out _));
            Assert.False(view.DeviceManager.ConnectButton.Enabled);
        });
    }

    [Fact]
    public void ConnectedDeviceCanSaveDraftWithoutAConnectionButtonAndRefreshDoesNotOverwriteIt()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            ManagedAndroidDevice device = new("device", "当前平板", "model", AndroidTransport.Usb, true, AndroidDeviceStatus.Ready,
                true, true, DateTimeOffset.UtcNow, "当前平板");
            view.ShowDeviceManager(device);
            ConnectionDeviceView dialog = view.DeviceManager;
            Assert.False(dialog.ConnectButton.Visible);
            Assert.False(dialog.RenameTextBox.ReadOnly);
            Assert.False(dialog.SaveButton.Enabled);
            dialog.RenameTextBox.Text = "修改平板";
            dialog.AutoConnectToggle.Checked = false;
            dialog.SetDevice(device, false, false);
            Assert.Equal("修改平板", dialog.RenameTextBox.Text);
            Assert.False(dialog.AutoConnectToggle.Checked);
            Assert.True(dialog.SaveButton.Enabled);
            Assert.True(dialog.TryGetDraft(out DevicePreferenceDraft? draft));
            dialog.SetBusy(true);
            Assert.False(dialog.SaveButton.Enabled);
            Assert.True(dialog.RenameTextBox.ReadOnly);
            dialog.SaveCompleted(draft!);
            dialog.SetBusy(false);
            Assert.False(dialog.HasChanges);
            Assert.Contains("已保存", dialog.Status.Text);
            Assert.False(dialog.SaveButton.Enabled);
        });
    }

    [Fact]
    public void DefaultNameAllowsSavingOnlyTheAutomaticPreference()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            view.ShowDeviceManager(new("device", "固件名称", "model", AndroidTransport.Usb, true, AndroidDeviceStatus.Ready, true, true, DateTimeOffset.UtcNow));
            view.DeviceManager.AutoConnectToggle.Checked = false;
            Assert.True(view.DeviceManager.SaveButton.Enabled);
            Assert.True(view.DeviceManager.TryGetDraft(out DevicePreferenceDraft? draft));
            Assert.Null(draft!.CustomName);
            Assert.False(draft.AutoConnect);
        });
    }

    [Fact]
    public void CodeOnlyModeNeverUsesHiddenAddressAndAllTabsShareTheDeviceList()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            var devices = view.DeviceList;
            Find<TextBox>(view, "pairEndpoint").Text = "192.168.1.20:30000";
            view.SelectMethod(WirelessPairingMethod.Code);
            Find<TextBox>(view, "autoPairCode").Text = "123456";
            Assert.Equal("", view.PairEndpoint);
            Assert.Equal("123456", view.PairingCode);
            Assert.False(view.AddressInputsVisible);
            Assert.Same(devices, view.DeviceList);
            view.SelectMethod(WirelessPairingMethod.Manual);
            Assert.Equal("192.168.1.20:30000", view.PairEndpoint);
            Assert.Equal("", view.PairingCode);
            Assert.True(view.AddressInputsVisible);
            Assert.Same(devices, view.DeviceList);
            view.SelectMethod(WirelessPairingMethod.Scan);
            Assert.False(view.AddressInputsVisible);
            Assert.Same(devices, view.DeviceList);
        });
    }

    [Fact]
    public void ManualFallbackAppearsOnlyOnRequestAndBusyPairingLocksTabSwitches()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            view.SelectMethod(WirelessPairingMethod.Code);
            Assert.False(view.ManualFallbackVisible);
            view.ShowManualFallback(true);
            Assert.True(view.ManualFallbackVisible);
            view.SetBusy(true);
            Assert.False(Find<WirelessPairingMethodSelectorControl>(view, "methodSelector").Enabled);
            Assert.False(view.AutoPairButton.Enabled);
            view.SetQrWaiting();
            Assert.True(Find<WirelessPairingMethodSelectorControl>(view, "methodSelector").Enabled);
            Assert.True(view.RefreshButton.Enabled);
            Assert.False(view.CancelButton.Visible);
            Assert.Empty(view.Controls.Find("qrRefreshButton", true));
            view.SetBusy(false);
            Assert.True(view.AutoPairButton.Enabled);
        });
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.IsType<T>(Assert.Single(root.Controls.Find(name, true)));

    private static void OnUiThread(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
