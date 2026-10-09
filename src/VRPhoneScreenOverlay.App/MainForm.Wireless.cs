using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private WirelessConnectionView _wirelessView = null!;
    private CancellationTokenSource? _wirelessOperation;

    private TaskCompletionSource? _wirelessCompletion;
    private bool _qrWaiting;
    private bool _switchingWireless;
    private bool _updatingConnectionMethod;
    private bool _changingConnectionMethod;
    private readonly PhoneConnectionManagementService? _connectionManagement;
    private ListBox _historyDeviceList = null!;
    private bool _openingDeviceManager;
    private bool _qrPausedForDevice;

    private void TryStartAutomaticQr()
    {
        if (IsDisposed || Disposing || _formLifetime.IsCancellationRequested || !_wirelessView.Visible ||
            _wirelessView.Method != WirelessPairingMethod.Scan || _wirelessView.DeviceManagerHost.Visible ||
            _wirelessOperation is not null || _operationInProgress || _changingConnectionMethod || _openingDeviceManager || _qrPausedForDevice) { return; }
        OnWirelessOperationClicked(_wirelessView, EventArgs.Empty);
    }

    private async void OnConnectionMethodChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingConnectionMethod || _changingConnectionMethod || _operationInProgress || _connectionManagement is null) { return; }
        PhoneConnectionMethod requestedMethod = (PhoneConnectionMethod)(int)_wirelessView.Method;
        _changingConnectionMethod = true;
        try
        {
            _wirelessView.SetManagementBusy(true);
            _wirelessOperation?.Cancel();
            if (_wirelessCompletion is { } completion) { await completion.Task; }
            _wirelessView.ShowQr(null);
            _wirelessView.HideDeviceManager();
            await RunUiOperationAsync(requestedMethod == PhoneConnectionMethod.Usb ? "正在扫描 USB 设备…" : "正在扫描无线设备…",
                cancellationToken => _connectionManagement.ChangeMethodAsync(requestedMethod, cancellationToken)).ConfigureAwait(true);
            if (!IsDisposed && !Disposing) { UpdatePhoneView(_phone.Snapshot); }
        }
        catch (Exception)
        {
            if (!IsDisposed && !Disposing) { _wirelessView.Status.Text = "设备扫描未完成，请刷新重试 [CONNECTION_METHOD_FAILED]"; }
        }
        finally
        {
            _changingConnectionMethod = false;
            if (!IsDisposed && !Disposing) { _wirelessView.SetManagementBusy(_operationInProgress); UpdatePhoneView(_phone.Snapshot); }
            TryStartAutomaticQr();
        }
    }

    private async void OnManagedDeviceSave(object? sender, EventArgs eventArgs)
    {
        if (_operationInProgress || !_wirelessView.DeviceManager.TryGetDraft(out DevicePreferenceDraft? draft) || draft is null) { return; }
        await RunUiOperationAsync("正在保存设备配置…", token => SaveDeviceDraftAsync(draft, token),
            resultTarget: _wirelessView.DeviceManager.Status).ConfigureAwait(true);
    }

    private async Task SaveDeviceDraftAsync(DevicePreferenceDraft draft, CancellationToken token)
    {
        if (_phone is not IAndroidConnectionManagementService management)
        { throw new AndroidConnectionException("CONNECTION_PREFERENCES_UNAVAILABLE", "当前服务不支持设备配置保存"); }
        await management.SaveDevicePreferencesAsync(draft.DeviceKey, draft.CustomName, draft.AutoConnect, token);
        if (!IsDisposed && !Disposing)
        {
            UpdatePhoneView(_phone.Snapshot);
            _wirelessView.DeviceManager.SaveCompleted(draft);
        }
    }
    private async void OnManagedDeviceConnect(object? sender, EventArgs eventArgs)
    {
        if (_connectionManagement is null || !CanSelectPhoneDevice || _wirelessView.DeviceManager.DeviceKey is not { } key) { return; }
        if (!_wirelessView.DeviceManager.TryGetDraft(out DevicePreferenceDraft? draft) || draft is null) { return; }
        bool connected = false;
        await RunUiOperationAsync("正在安全连接设备…", async token =>
        {
            if (_wirelessView.DeviceManager.HasChanges) { await SaveDeviceDraftAsync(draft, token); }
            await _connectionManagement.ConnectAsync(key, token);
            connected = true;
        },
            resultTarget: _wirelessView.DeviceManager.Status).ConfigureAwait(true);
        if (!IsDisposed && !Disposing) { if (connected) { _wirelessView.HideDeviceManager(); } UpdatePhoneView(_phone.Snapshot); }
    }

    private async void OnManagedDeviceForget(object? sender, EventArgs eventArgs)
    {
        if (_connectionManagement is null || !CanSelectPhoneDevice || _wirelessView.DeviceManager.DeviceKey is not { } key) { return; }
        bool forgotten = false;
        await RunUiOperationAsync("正在忘记设备…", async token =>
        { await _connectionManagement.ForgetAsync(key, token); forgotten = true; }, resultTarget: _wirelessView.DeviceManager.Status).ConfigureAwait(true);
        if (!IsDisposed && !Disposing)
        {
            if (forgotten) { _wirelessView.HideDeviceManager(); _operationStatus.Text = "已忘记设备，自动连接已关闭"; }
            UpdatePhoneView(_phone.Snapshot);
        }
    }


    private bool CanChangePhoneTransport => !_operationInProgress && _wirelessOperation is null &&
        _phoneOverlay.Snapshot.State is PhoneOverlayState.Stopped or PhoneOverlayState.Faulted;

    private bool CanSelectPhoneDevice => !_operationInProgress && (_wirelessOperation is null || _qrWaiting) && !_switchingWireless && !_changingConnectionMethod;

    private async void OnWirelessOperationClicked(object? sender, EventArgs eventArgs)
    {
        if (_operationInProgress || _wirelessView.Method == WirelessPairingMethod.Usb ||
            _switchingWireless || (_wirelessOperation is not null && !_qrWaiting)) { return; }
        _switchingWireless = true;
        try
        {
            if (_wirelessOperation is not null)
            {
                await _wirelessOperation.CancelAsync();
                if (_wirelessCompletion is { } completion) { await completion.Task; }
            }
        }
        catch (Exception)
        {
            if (!IsDisposed && !Disposing) { _wirelessView.Status.Text = "配对切换未完成，请重试 [WIRELESS_SWITCH_FAILED]"; }
            return;
        }
        finally { _switchingWireless = false; }
        if (IsDisposed || Disposing || _formLifetime.IsCancellationRequested) { return; }
        _wirelessCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        WirelessPairingMethod operationMethod = _wirelessView.Method;
        try
        {
            if (_phone is not IAndroidWirelessConnectionService wireless)
            {
                _wirelessView.Status.Text = "当前连接服务不支持无线配对";
                return;
            }
            WirelessAdbOperation operation = ReferenceEquals(sender, _wirelessView)
                ? WirelessAdbOperation.PairQr : (ReferenceEquals(sender, _wirelessView.PairButton) || ReferenceEquals(sender, _wirelessView.AutoPairButton))
                ? WirelessAdbOperation.Pair
                : ReferenceEquals(sender, _wirelessView.ConnectButton)
                    ? WirelessAdbOperation.Connect : WirelessAdbOperation.Disconnect;
            if (operation == WirelessAdbOperation.Disconnect && !CanChangePhoneTransport)
            {
                _wirelessView.Status.Text = "请先返回主页关闭手机浮窗，再断开连接";
                return;
            }
            string endpoint = operation == WirelessAdbOperation.Pair
                ? _wirelessView.PairEndpoint : _wirelessView.ConnectEndpoint;
            if (operation == WirelessAdbOperation.Pair && _wirelessView.Method == WirelessPairingMethod.Manual && string.IsNullOrWhiteSpace(endpoint))
            {
                _wirelessView.Status.Text = "请填写手机配对窗口中的 IP 地址和配对端口；只输入配对码请选择“配对码配对”。";
                return;
            }
            _wirelessView.ShowManualFallback(false);
            string code = _wirelessView.PairingCode;
            _wirelessView.ClearPairingCode();
            _wirelessView.ShowQr(null);
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(_formLifetime.Token);
            _wirelessOperation = cancellation;
            _wirelessView.SetBusy(true);
            _wirelessView.CancelButton.Text = operation is WirelessAdbOperation.Pair or WirelessAdbOperation.PairQr ? "取消配对" : "取消连接";
            _qrWaiting = operation == WirelessAdbOperation.PairQr;
            if (_qrWaiting) { _wirelessView.SetQrWaiting(); }
            UpdatePhoneOverlayButton();
            _wirelessView.Status.ForeColor = UiPalette.Info;
            _wirelessView.Status.Text = _qrWaiting ? "等待扫码" : operation == WirelessAdbOperation.Pair
                ? "正在配对…" : "正在连接…";
            AndroidOperationResult result = operation == WirelessAdbOperation.PairQr
                ? await WirelessQrPairingFlow.RunContinuousAsync(wireless, (qr, refreshed) =>
                {
                    _wirelessView.ShowQr(qr.Payload);
                    if (refreshed) { _wirelessView.Status.Text = "等待扫码"; }
                }, () => !IsDisposed && !Disposing && _wirelessView.Visible &&
                    _wirelessView.Method == WirelessPairingMethod.Scan && !_qrPausedForDevice,
                    result => { _wirelessView.Status.Text = result.Message; }, cancellation.Token)
                : await wireless.ExecuteWirelessAsync(operation, endpoint, code, cancellation.Token);
            if (IsDisposed || Disposing || _wirelessView.Method != operationMethod) { return; }
            _wirelessView.ShowManualFallback(!result.Succeeded || result.ReasonCode == "WIRELESS_PAIRED");
            _wirelessView.Status.Text = result.Succeeded ? result.ReasonCode == "WIRELESS_PAIRED"
                ? "已配对，连接未完成" : result.ReasonCode == "WIRELESS_CONNECTED" ? "配对成功，请在下方连接此设备"
                : operation == WirelessAdbOperation.Disconnect ? "已断开" : "已连接"
                : result.Message;
            _wirelessView.Status.ForeColor = result.Succeeded ? UiPalette.Success : UiPalette.Warning;
        }
        catch (OperationCanceledException)
        {
            if (!IsDisposed && !Disposing && _wirelessView.Method == operationMethod) { _wirelessView.Status.Text = "无线连接操作已取消"; }
        }
        catch (Exception)
        {
            if (!IsDisposed && !Disposing)
            {
                _wirelessView.Status.Text = "无线连接暂不可用，请刷新后重试 [WIRELESS_UI_FAILED]";
                _wirelessView.Status.ForeColor = UiPalette.Danger;
            }
        }
        finally
        {
            _wirelessOperation = null;
            _qrWaiting = false;
            _wirelessCompletion?.TrySetResult();
            if (!IsDisposed && !Disposing)
            {
                _wirelessView.ShowQr(null);
                _wirelessView.SetBusy(false);
                _refreshButton.Enabled = !_operationInProgress;
                UpdatePhoneOverlayButton();
            }
        }
    }
}
