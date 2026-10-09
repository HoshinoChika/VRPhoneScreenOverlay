using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Session;

/// <summary>Release the owned media session before changes to connection selection.</summary>
public sealed class PhoneConnectionManagementService(IAndroidConnectionService phone,
    IAndroidConnectionManagementService management, IPhoneMediaSessionCoordinator media,
    Func<AndroidVideoOptions> videoOptions, Func<bool>? autoOpenAfterConnect = null)
{
    private bool AutoOpenAfterConnect => autoOpenAfterConnect?.Invoke() == true;
    public async Task ChangeMethodAsync(PhoneConnectionMethod method, CancellationToken cancellationToken)
    {
        bool preserve = media.Snapshot.State is PhoneMediaSessionState.Running or PhoneMediaSessionState.Starting or
            PhoneMediaSessionState.PausingForScreenRestart or PhoneMediaSessionState.ScreenRestartPaused or PhoneMediaSessionState.ResumingAfterScreenRestart;
        await management.SetConnectionMethodAsync(method, preserve, cancellationToken).ConfigureAwait(false);
        if (!preserve) { media.RequestAutomaticStart(); }
    }

    public async Task ConnectAsync(string deviceKey, CancellationToken cancellationToken)
    {
        ManagedAndroidDevice? device = management.ManagementSnapshot.Devices.FirstOrDefault(value => value.DeviceKey == deviceKey);
        if (device is null) { throw new AndroidConnectionException("CONNECTION_DEVICE_UNKNOWN", "设备已不在列表中，请刷新设备"); }
        bool wireless = management.ManagementSnapshot.Method != PhoneConnectionMethod.Usb;
        if (wireless != (device.Transport == AndroidTransport.Network))
        { throw new AndroidConnectionException("CONNECTION_METHOD_MISMATCH", "请先选择与此设备对应的 USB 或无线连接方式"); }
        if (phone.Snapshot.Devices.Any(device => device.DeviceKey == deviceKey && device.Status == AndroidDeviceStatus.Ready))
        {
            await media.SwitchDeviceAsync(deviceKey, videoOptions, cancellationToken).ConfigureAwait(false);
            if (!phone.Snapshot.IsReady || phone.Snapshot.SelectedDevice?.DeviceKey != deviceKey)
            { throw new AndroidConnectionException("CONNECTION_DEVICE_NOT_READY", "设备尚未连接成功，请刷新后重试"); }
            if (AutoOpenAfterConnect && !IsActive)
            { await media.StartAsync(deviceKey, videoOptions(), cancellationToken).ConfigureAwait(false); }
            return;
        }
        bool reopen = IsActive || AutoOpenAfterConnect;
        await media.StopForConnectionChangeAsync(cancellationToken).ConfigureAwait(false);
        await management.ConnectKnownDeviceAsync(deviceKey, cancellationToken).ConfigureAwait(false);
        if (!phone.Snapshot.IsReady)
        { throw new AndroidConnectionException("CONNECTION_DEVICE_NOT_READY", "设备尚未连接成功，请刷新后重试"); }
        if (reopen) { await media.StartAsync(phone.Snapshot.SelectedDevice?.DeviceKey, videoOptions(), cancellationToken).ConfigureAwait(false); }
    }

    public async Task ForgetAsync(string deviceKey, CancellationToken cancellationToken)
    {
        if (phone.Snapshot.SelectedDevice?.DeviceKey == deviceKey || management.IsSelectedDevice(deviceKey)) { await media.StopAsync(cancellationToken).ConfigureAwait(false); }
        await management.ForgetDeviceAsync(deviceKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await media.StopAsync(cancellationToken).ConfigureAwait(false);
        await management.DisconnectSelectionAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool IsActive => media.Snapshot.State is PhoneMediaSessionState.Running or PhoneMediaSessionState.Starting or PhoneMediaSessionState.WaitingForDevice;
}
