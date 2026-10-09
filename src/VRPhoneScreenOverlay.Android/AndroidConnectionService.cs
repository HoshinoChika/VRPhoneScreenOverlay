namespace VRPhoneScreenOverlay.Android;

internal sealed partial class AndroidConnectionService : IAndroidConnectionService, IAndroidWirelessConnectionService, IAndroidConnectionManagementService
{
    private readonly string _resourceDirectory;
    private readonly IAndroidConnectionLogSink _log;
    private readonly AndroidConnectionServiceOptions _options;
    private readonly IAdbServerShutdown _adbServerShutdown;
    private readonly WirelessAdbConnector _wireless;
    private readonly WirelessAdbReconnect _wirelessReconnect;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _snapshotGate = new();
    private readonly CancellationTokenSource _stopSource = new();
    // One full wireless transaction, zero queued requests; reject concurrent callers.
    private readonly SemaphoreSlim _wirelessOperationGate = new(1, 1);
    private Task? _discoveryTask;
    private IAdbClient? _client;
    private AndroidConnectionSnapshot _snapshot;
    private Dictionary<string, AdbDeviceRecord> _devicesByKey =
        new Dictionary<string, AdbDeviceRecord>(StringComparer.Ordinal);
    // Discovery-owned metadata cache, capacity 32. Remove absent entries first,
    // then evict the oldest inserted entry; optional reads retry after 30 seconds.
    private readonly Dictionary<string, (AdbProbeResult? Probe, long RetryAt)> _deviceMetadata = new(StringComparer.Ordinal);
    private string? _preferredDeviceKey;
    private string? _probedDeviceKey;
    private AndroidDeviceDetails? _selectedDetails;
    private string? _lastScanSignature;
    private DateTimeOffset _lastScanLogAt;
    private string? _activeDeviceKey;
    private string? _activeSerial;
    private long _sessionEpoch;
    private bool _disposed;

    public AndroidConnectionService(
        string resourceDirectory,
        IAndroidConnectionLogSink log,
        AndroidConnectionServiceOptions options)
        : this(
            resourceDirectory,
            log,
            options,
            new BundledAdbServerShutdown(
                resourceDirectory,
                (options ?? throw new ArgumentNullException(nameof(options))).CommandTimeout))
    {
    }

    internal AndroidConnectionService(
        string resourceDirectory,
        IAndroidConnectionLogSink log,
        AndroidConnectionServiceOptions options,
        IAdbServerShutdown adbServerShutdown,
        Func<CancellationToken, ValueTask<IAdbCommandRunner>>? wirelessRunnerFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceDirectory);
        _resourceDirectory = Path.GetFullPath(resourceDirectory);
        _wireless = new WirelessAdbConnector(wirelessRunnerFactory ?? CreateWirelessRunnerAsync);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _wirelessReconnect = new(options.WirelessReconnectPath, wirelessRunnerFactory ?? CreateWirelessRunnerAsync);
        _connectionPreferences = new(options.ConnectionPreferencesPath);
        _managedRunnerFactory = wirelessRunnerFactory ?? CreateWirelessRunnerAsync;
        _adbServerShutdown = adbServerShutdown ??
            throw new ArgumentNullException(nameof(adbServerShutdown));
        _snapshot = new AndroidConnectionSnapshot(
            0,
            AndroidConnectionState.Created,
            AndroidReasonCodes.Created,
            "手机连接服务已创建",
            [],
            null,
            DateTimeOffset.UtcNow,
            null);
    }

    public AndroidConnectionSnapshot Snapshot
    {
        get
        {
            lock (_snapshotGate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<AndroidConnectionChangedEventArgs>? StateChanged;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_discoveryTask is not null)
        {
            return ValueTask.CompletedTask;
        }

        Publish(
            AndroidConnectionState.Discovering,
            AndroidReasonCodes.Discovering,
            "正在检查内置连接组件并查找手机",
            [],
            null,
            null);
        _discoveryTask = RunDiscoveryLoopAsync(_stopSource.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await ScanOnceAsync(manualRefresh: true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SelectDeviceAsync(
        string deviceKey,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Snapshot.Devices.Any(device =>
                string.Equals(device.DeviceKey, deviceKey, StringComparison.Ordinal)))
        {
            return;
        }
        if (_connectionPreferences.Enabled && Snapshot.Devices.First(device => device.DeviceKey == deviceKey) is { } requested &&
            !ConnectionPreferencesStore.Supports(_connectionPreferences.Value.Method, requested.Transport))
        { throw new AndroidConnectionException("CONNECTION_METHOD_MISMATCH", "请先选择与此设备对应的 USB 或无线连接方式"); }

        if (_activeDeviceKey != deviceKey) { MarkTransportUnavailable(); }
        _preferredDeviceKey = deviceKey;
        _manualDeviceKey = deviceKey;
        _automaticSelectionSuppressed = false;
        _probedDeviceKey = null;
        await ScanOnceAsync(manualRefresh: true, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IAdbCommandRunner> CreateWirelessRunnerAsync(CancellationToken cancellationToken)
    {
        AndroidResourceValidationResult validation = await AndroidResourceValidator.ValidateAsync(
            _resourceDirectory, cancellationToken).ConfigureAwait(false);
        if (!validation.Succeeded)
        {
            throw new AndroidConnectionException(validation.ReasonCode, validation.Message);
        }
        // A pairing client may start the shared daemon. Cancelling this request
        // must not kill that daemon and tear down unrelated USB/media sessions.
        return new AdbCommandRunner(validation.ExecutablePath, killProcessTree: false);
    }

    public async ValueTask<AndroidOperationResult> ExecuteWirelessAsync(
        WirelessAdbOperation operation, string endpoint, string pairingCode,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!await _wirelessOperationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new(false, "WIRELESS_BUSY", "无线连接操作正在进行，请稍候");
        }
        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _stopSource.Token);
            try
            {
                AndroidOperationResult result = await ExecuteWirelessCoreAsync(
                    operation, endpoint, pairingCode, linked.Token).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                return new(false, "WIRELESS_CANCELLED", "无线连接操作已取消");
            }
            catch (Exception exception) when (exception is AndroidConnectionException or IOException or
                UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return new(false, "WIRELESS_STATUS_FAILED", "无线连接状态检查失败，请刷新设备后重试");
            }
        }
        finally
        {
            _wirelessOperationGate.Release();
        }
    }

    private async ValueTask<AndroidOperationResult> ExecuteWirelessCoreAsync(
        WirelessAdbOperation operation, string endpoint, string pairingCode,
        CancellationToken cancellationToken)
    {
        if (operation == WirelessAdbOperation.Disconnect)
        {
            // Stop automatic attempts before disconnecting, under discovery ownership.
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await _wirelessReconnect.ForgetAsync(cancellationToken).ConfigureAwait(false); }
            finally { _operationGate.Release(); }
        }
        AndroidOperationResult result = await _wireless.ExecuteAsync(
            operation, endpoint, pairingCode, cancellationToken, autoConnect: true).ConfigureAwait(false);
        if (!result.Succeeded || _disposed || cancellationToken.IsCancellationRequested) { return result; }
        if (result.ConnectedEndpoint is { } connectedEndpoint)
        {
            endpoint = connectedEndpoint;
            operation = WirelessAdbOperation.Connect;
        }
        if (operation != WirelessAdbOperation.Disconnect)
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { _wirelessReconnect.Resume(); }
            finally { _operationGate.Release(); }
        }
        string? requestedKey = null;
        if (operation == WirelessAdbOperation.Connect &&
            WirelessAdbEndpoint.TryNormalize(endpoint, out string normalized))
        {
            requestedKey = AdbOutputParser.CreateDeviceKey(normalized);
            // Connecting a second transport must not steal a running phone session.
            // Keep the current ready device; the visible list allows an explicit switch.
            if (!Snapshot.IsReady && !_connectionPreferences.Enabled)
            { _preferredDeviceKey = _manualDeviceKey = requestedKey; _automaticSelectionSuppressed = false; }
        }
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (operation == WirelessAdbOperation.Connect)
        {
            AndroidDeviceView? ready = Snapshot.Devices.FirstOrDefault(device => device.Transport == AndroidTransport.Network &&
                device.Status == AndroidDeviceStatus.Ready &&
                string.Equals(device.DeviceKey, requestedKey, StringComparison.Ordinal));
            if (ready is null && _client is not null)
            {
                // Auto-connected TLS transports are listed by their mDNS service name,
                // not necessarily by the endpoint passed to "connect". Resolve only
                // this requested endpoint; never infer identity from another ready phone.
                string? service = await _client.ResolveWirelessServiceAsync(endpoint, cancellationToken)
                    .ConfigureAwait(false);
                if (service is not null)
                {
                    lock (_snapshotGate)
                    {
                        string? key = _devicesByKey.Values.FirstOrDefault(device =>
                            string.Equals(device.Serial.TrimEnd('.'), service, StringComparison.Ordinal))?.DeviceKey;
                        ready = _snapshot.Devices.FirstOrDefault(device => device.DeviceKey == key &&
                            device.Transport == AndroidTransport.Network && device.Status == AndroidDeviceStatus.Ready);
                    }
                }
            }
            AndroidConnectionSnapshot current = Snapshot;
            if (_connectionPreferences.Enabled && ready is not null)
            {
                await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    AdbDeviceRecord? connected;
                    lock (_snapshotGate) { _devicesByKey.TryGetValue(ready.DeviceKey, out connected); }
                    if (connected is not null)
                    {
                        await _connectionPreferences.RememberAsync(connected, ready.DisplayName, ready.Model,
                            null, endpoint, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { WriteLog("connection_history", "CONNECTION_HISTORY_SAVE_FAILED", "连接已建立，但历史记录保存失败"); }
                finally { _operationGate.Release(); }
            }
            if (ready is { IsSelected: true } && current.IsReady &&
                current.SelectedDevice?.DeviceKey == ready.DeviceKey)
            {
                return new(true, "WIRELESS_READY", "无线手机已就绪；返回主页即可打开浮窗");
            }
            if (ready is { IsSelected: false })
            {
                return new(true, "WIRELESS_CONNECTED", "无线连接已建立，请在连接管理里选择该设备");
            }
            return new(false, "WIRELESS_NOT_READY", "无线连接已建立，但手机尚未就绪；请检查授权或刷新设备列表");
        }
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PublishFromCurrent(
            AndroidConnectionState.Stopping,
            AndroidReasonCodes.Stopping,
            "正在停止手机连接服务");
        await _stopSource.CancelAsync().ConfigureAwait(false);
        await _wireless.DisposeAsync().ConfigureAwait(false);
        await _wirelessOperationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _wirelessOperationGate.Release();

        if (_discoveryTask is not null)
        {
            try
            {
                await _discoveryTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _operationGate.Release();

        AdbServerShutdownResult adbShutdown;
        try
        {
            adbShutdown = await _adbServerShutdown.StopAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            adbShutdown = new AdbServerShutdownResult(
                false,
                AndroidReasonCodes.AdbServerStopFailed,
                $"内置 ADB 后台服务退出失败（{exception.GetType().Name}）");
        }

        WriteLog(
            "adb_server_shutdown",
            adbShutdown.ReasonCode,
            adbShutdown.Message);

        PublishFromCurrent(
            AndroidConnectionState.Stopped,
            AndroidReasonCodes.Stopped,
            "手机连接服务已停止");
        _stopSource.Dispose();
        _wirelessOperationGate.Dispose();
        _operationGate.Dispose();
    }

    internal async ValueTask ScanOnceForTestAsync(CancellationToken cancellationToken) =>
        await ScanOnceAsync(manualRefresh: true, cancellationToken).ConfigureAwait(false);

    internal void SetClientForTest(IAdbClient client)
    {
        _client = client;
    }

    internal bool TryResolveReadyDevice(
        string? requestedDeviceKey,
        out ResolvedAndroidDevice resolved)
    {
        lock (_snapshotGate)
        {
            string? key = string.IsNullOrWhiteSpace(requestedDeviceKey)
                ? _snapshot.SelectedDevice?.DeviceKey
                : requestedDeviceKey;
            if (_snapshot.State == AndroidConnectionState.Ready &&
                key is not null &&
                _sessionEpoch > 0 &&
                _devicesByKey.TryGetValue(key, out AdbDeviceRecord? device) &&
                device.Status == AndroidDeviceStatus.Ready &&
                string.Equals(_activeDeviceKey, device.DeviceKey, StringComparison.Ordinal) &&
                string.Equals(_activeSerial, device.Serial, StringComparison.Ordinal))
            {
                resolved = new ResolvedAndroidDevice(
                    device.DeviceKey,
                    device.Serial,
                    DisplayName(device),
                    _sessionEpoch);
                return true;
            }
        }

        resolved = default;
        return false;
    }

    private async Task RunDiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await ScanOnceAsync(manualRefresh: false, cancellationToken).ConfigureAwait(false);
            TimeSpan interval = Snapshot.State switch
            {
                AndroidConnectionState.Ready => _options.ReadyScanInterval,
                AndroidConnectionState.Faulted => _options.FaultedScanInterval,
                _ => _options.SearchingScanInterval,
            };

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask ScanOnceAsync(bool manualRefresh, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) { return; }
            cancellationToken.ThrowIfCancellationRequested();
            await InitializeConnectionPreferencesAsync(cancellationToken).ConfigureAwait(false);
            if (_client is null)
            {
                AndroidResourceValidationResult validation =
                    await AndroidResourceValidator.ValidateAsync(_resourceDirectory, cancellationToken)
                        .ConfigureAwait(false);
                WriteLog(
                    "resource_validation",
                    validation.ReasonCode,
                    validation.Message,
                    state: validation.Succeeded
                        ? AndroidConnectionState.Discovering
                        : AndroidConnectionState.Faulted);
                if (!validation.Succeeded)
                {
                    MarkTransportUnavailable();
                    Publish(
                        AndroidConnectionState.Faulted,
                        validation.ReasonCode,
                        validation.Message,
                        [],
                        null,
                        null);
                    return;
                }

                _client = new AdbClient(
                    new AdbCommandRunner(validation.ExecutablePath),
                    _options.CommandTimeout);
            }

            if (manualRefresh && Snapshot.State != AndroidConnectionState.Ready)
            {
                PublishFromCurrent(
                    AndroidConnectionState.Discovering,
                    AndroidReasonCodes.Refreshing,
                    "正在重新扫描手机");
            }

            AdbListResult list = await _client.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
            if (_connectionPreferences.Enabled && _wireless.IsWaitingForQr)
            {
                if (await TryManagedReconnectAsync(list, cancellationToken).ConfigureAwait(false))
                {
                    MarkTransportUnavailable();
                    list = await _client.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else if (await _wirelessOperationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    bool reconnected = _connectionPreferences.Enabled
                        ? await TryManagedReconnectAsync(list, cancellationToken).ConfigureAwait(false)
                        : await _wirelessReconnect.ObserveAsync(list, cancellationToken).ConfigureAwait(false);
                    if (reconnected)
                    {
                        if (_connectionPreferences.Enabled)
                        {
                            // A gap recovered inside one scan must still invalidate old media ownership.
                            MarkTransportUnavailable();
                            _probedDeviceKey = null;
                        }
                        list = await _client.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                finally { _wirelessOperationGate.Release(); }
            }
            AdbDeviceRecord[] supportedDevices = WirelessAdbDeviceAliases.Collapse(
                list.Devices, list.WirelessServices, _activeDeviceKey ?? _preferredDeviceKey)
                .Where(device => device.Transport is AndroidTransport.Usb or AndroidTransport.Network)
                .Select(device => WithPhysicalIdentity(device, list.WirelessServices)).ToArray();
            if (_connectionPreferences.Enabled)
            {
                supportedDevices = CanonicalizeManagedConnections(supportedDevices, list.WirelessServices);
                await ReconcilePreferencesAsync([.. list.Devices.Select(device => WithPhysicalIdentity(device, list.WirelessServices)), .. supportedDevices], list.WirelessServices, cancellationToken).ConfigureAwait(false);
            }
            foreach (string key in _deviceMetadata.Keys.Where(key => !supportedDevices.Any(device => device.DeviceKey == key && device.Status == AndroidDeviceStatus.Ready)).ToArray())
            {
                _deviceMetadata.Remove(key);
            }
            DateTimeOffset scanTime = DateTimeOffset.UtcNow;
            lock (_snapshotGate)
            {
                _lastWirelessServices = list.WirelessServices;
                _devicesByKey = supportedDevices.ToDictionary(
                    device => device.DeviceKey,
                    StringComparer.Ordinal);
            }
            string scanSignature = string.Join(
                ';',
                supportedDevices.Select(device =>
                    $"{device.DeviceKey}|{device.Status}|{device.Transport}|{device.Model}"));
            if (!string.Equals(scanSignature, _lastScanSignature, StringComparison.Ordinal) ||
                scanTime - _lastScanLogAt >= TimeSpan.FromMinutes(1))
            {
                _lastScanSignature = scanSignature;
                _lastScanLogAt = scanTime;
                WriteLog(
                    "device_scan",
                    AndroidReasonCodes.ScanOk,
                    "手机设备扫描完成",
                    deviceCount: supportedDevices.Length,
                    duration: list.Duration,
                    toolVersion: list.ToolVersion);
            }

            AdbDeviceRecord? selected;
            if (_connectionPreferences.Enabled)
            {
                AdbDeviceRecord[] compatible = supportedDevices.Where(device => device.Status == AndroidDeviceStatus.Ready &&
                    ConnectionPreferencesStore.Supports(_connectionPreferences.Value.Method, device.Transport)).ToArray();
                // A healthy owned route survives browsing another transport. A lost
                // route falls back only to automatic candidates in the viewed method.
                selected = supportedDevices.FirstOrDefault(device => device.Status == AndroidDeviceStatus.Ready && device.DeviceKey == _activeDeviceKey) ??
                    compatible.FirstOrDefault(device => device.DeviceKey == _manualDeviceKey) ??
                    (_automaticSelectionSuppressed ? null : ConnectionPreferencesStore.SelectAutomatic(compatible, AutomaticPreferences));
            }
            else { selected = AndroidDeviceSelector.Select(supportedDevices, _preferredDeviceKey); }
            if (selected is null)
            {
                await EnrichOneDeviceNameAsync(supportedDevices, null, cancellationToken).ConfigureAwait(false);
                await ReconcilePhysicalDevicesAsync(supportedDevices, list.Devices, list.WirelessServices, cancellationToken).ConfigureAwait(false);
                MarkTransportUnavailable();
                _selectedDetails = null;
                _probedDeviceKey = null;
                PublishNoReadyDevice(supportedDevices, scanTime);
                return;
            }

            _preferredDeviceKey ??= selected.DeviceKey;
            if (!string.Equals(_probedDeviceKey, selected.DeviceKey, StringComparison.Ordinal))
            {
                Publish(
                    AndroidConnectionState.Connecting,
                    AndroidReasonCodes.Probing,
                    $"正在读取 {DisplayName(selected)} 的系统信息",
                    CreateViews(supportedDevices, selected.DeviceKey),
                    null,
                    scanTime);
                AdbProbeResult probe = await _client.ProbeAsync(selected.Serial, cancellationToken)
                    .ConfigureAwait(false);
                WriteLog(
                    "device_probe",
                    probe.ReasonCode,
                    probe.Message,
                    selected.DeviceKey,
                    probe.Succeeded ? AndroidConnectionState.Ready : AndroidConnectionState.Reconnecting,
                    duration: probe.Duration,
                    deviceModel: probe.Model,
                    androidVersion: probe.AndroidVersion,
                    androidSdk: probe.AndroidSdk,
                    cpuAbi: probe.CpuAbi);
                if (!probe.Succeeded)
                {
                    MarkTransportUnavailable();
                    Publish(
                        AndroidConnectionState.Reconnecting,
                        probe.ReasonCode,
                        probe.Message,
                        CreateViews(supportedDevices, selected.DeviceKey),
                        null,
                        scanTime);
                    return;
                }

                RememberMetadata(selected.DeviceKey, probe);
                _probedDeviceKey = selected.DeviceKey;
                _selectedDetails = new AndroidDeviceDetails(
                    selected.DeviceKey,
                    probe.Manufacturer,
                    probe.Brand,
                    string.IsNullOrWhiteSpace(probe.Model) ? selected.Model : probe.Model,
                    probe.DeviceCodeName,
                    probe.AndroidVersion,
                    probe.AndroidSdk,
                    probe.CpuAbi,
                    selected.Transport)
                {
                    Capabilities = AndroidCapabilityEvaluator.Evaluate(probe.AndroidSdk),
                    NativeDisplayWidth = probe.DisplayWidth,
                    NativeDisplayHeight = probe.DisplayHeight,
                };
            }

            // At most one extra, authorized phone per scan. Bound optional metadata
            // work independently so one unresponsive secondary phone cannot block readiness.
            await EnrichOneDeviceNameAsync(supportedDevices, selected.DeviceKey, cancellationToken).ConfigureAwait(false);
            await ReconcilePhysicalDevicesAsync(supportedDevices, list.Devices, list.WirelessServices, cancellationToken).ConfigureAwait(false);
            selected = WithPhysicalIdentity(selected, list.WirelessServices);
            if (_connectionPreferences.Enabled && _activeDeviceKey != selected.DeviceKey)
            {
                try
                {
                    await _connectionPreferences.RememberAsync(selected, OriginalDisplayName(selected), _selectedDetails?.Model ?? selected.Model,
                        list.WirelessServices, null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { WriteLog("connection_history", "CONNECTION_HISTORY_SAVE_FAILED", "连接已建立，但历史记录保存失败"); }
            }
            ActivateTransport(selected);
            _automaticDeviceKeys.IntersectWith(_connectionPreferences.Value.Devices.Where(device => device.AutoConnect).Select(ConnectionPreferencesStore.Scope));
            if (_connectionPreferences.Value.Devices.Any(device => device.DeviceKey == selected.DeviceKey && device.AutoConnect))
            { _automaticDeviceKeys.Add(ConnectionPreferencesStore.Scope(AndroidPhysicalDeviceIdentity.Current(selected, list.WirelessServices, _connectionPreferences.Value), selected.Transport)); }
            int readyCount = supportedDevices.Count(device => device.Status == AndroidDeviceStatus.Ready);
            string message = readyCount > 1
                ? $"已连接 {DisplayName(selected)}，共发现 {readyCount} 条可用连接"
                : $"已连接 {DisplayName(selected)} · {AndroidDisplayNames.Transport(selected.Transport)}";
            Publish(AndroidConnectionState.Ready, readyCount > 1 ? AndroidReasonCodes.ReadyMultiple : AndroidReasonCodes.Ready,
                message, CreateViews(supportedDevices, selected.DeviceKey), _selectedDetails, scanTime);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (AndroidConnectionException exception)
        {
            MarkTransportUnavailable();
            AndroidConnectionState state = Snapshot.IsReady ? AndroidConnectionState.Reconnecting : AndroidConnectionState.Faulted;
            WriteLog("scan_error", exception.ReasonCode, exception.Message, state: state);
            PublishFromCurrent(state, exception.ReasonCode, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MarkTransportUnavailable();
            const string reasonCode = AndroidReasonCodes.ScanUnexpected;
            string message = "手机连接扫描失败，程序将自动重试";
            AndroidConnectionState state = Snapshot.IsReady ? AndroidConnectionState.Reconnecting : AndroidConnectionState.Faulted;
            WriteLog("scan_error", reasonCode, $"{message} ({exception.GetType().Name})", state: state);
            PublishFromCurrent(state, reasonCode, message);
        }
        finally { _operationGate.Release(); }
    }

    private async ValueTask EnrichOneDeviceNameAsync(IReadOnlyList<AdbDeviceRecord> supportedDevices,
        string? selectedKey, CancellationToken cancellationToken)
    {
        AdbDeviceRecord? pending = supportedDevices.FirstOrDefault(device => device.DeviceKey != selectedKey &&
            device.Status == AndroidDeviceStatus.Ready && (!_deviceMetadata.TryGetValue(device.DeviceKey, out var entry) ||
                (entry.Probe is null && Environment.TickCount64 >= entry.RetryAt)));
        if (pending is not null)
        {
            using CancellationTokenSource metadataDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            metadataDeadline.CancelAfter(TimeSpan.FromSeconds(3));
            AdbProbeResult? metadata = null;
            try
            {
                AdbProbeResult candidate = await _client!.ProbeAsync(pending.Serial, metadataDeadline.Token).ConfigureAwait(false);
                if (candidate.Succeeded) { metadata = candidate; }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) when (exception is AndroidConnectionException or IOException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception)
            { }
            RememberMetadata(pending.DeviceKey, metadata);
        }
    }

    private void PublishNoReadyDevice(
        IReadOnlyList<AdbDeviceRecord> devices,
        DateTimeOffset scanTime)
    {
        IReadOnlyList<AdbDeviceRecord> visible = _connectionPreferences.Enabled
            ? devices.Where(device => ConnectionPreferencesStore.Supports(_connectionPreferences.Value.Method, device.Transport)).ToArray() : devices;
        if (_connectionPreferences.Enabled && visible.Any(device => device.Status == AndroidDeviceStatus.Ready))
        {
            Publish(AndroidConnectionState.WaitingForDevice, "CONNECTION_SELECTION_REQUIRED",
                "未发现符合自动连接条件的设备，可在当前列表中手动连接", CreateViews(devices, null), null, scanTime);
            return;
        }
        AdbDeviceRecord? unauthorized = visible.FirstOrDefault(device =>
            device.Status == AndroidDeviceStatus.Unauthorized);
        if (unauthorized is not null)
        {
            Publish(
                AndroidConnectionState.AuthorizationRequired,
                AndroidReasonCodes.AuthorizationRequired,
                unauthorized.Transport == AndroidTransport.Network
                    ? "无线手机未授权，请在手机无线调试中重新配对这台电脑"
                    : "已发现手机：请解锁手机，勾选始终允许，然后允许 USB 调试",
                CreateViews(devices, null),
                null,
                scanTime);
            return;
        }

        AdbDeviceRecord? noPermissions = visible.FirstOrDefault(device =>
            device.Status == AndroidDeviceStatus.NoPermissions);
        if (noPermissions is not null)
        {
            Publish(
                AndroidConnectionState.Faulted,
                AndroidReasonCodes.DriverPermissionRequired,
                "电脑没有访问手机的驱动权限，请检查 USB 驱动",
                CreateViews(devices, null),
                null,
                scanTime);
            return;
        }

        AdbDeviceRecord? offline = visible.FirstOrDefault(device =>
            device.Status == AndroidDeviceStatus.Offline);
        if (offline is not null)
        {
            Publish(
                AndroidConnectionState.Offline,
                AndroidReasonCodes.DeviceOffline,
                offline.Transport == AndroidTransport.Network
                    ? "无线手机离线，请检查 Wi-Fi 和无线调试，使用当前连接端口重连"
                    : "手机连接离线，请重新插拔 USB 并确认 USB 调试仍已开启",
                CreateViews(devices, null),
                null,
                scanTime);
            return;
        }

        Publish(
            AndroidConnectionState.WaitingForDevice,
            AndroidReasonCodes.WaitingForDevice,
            "等待手机连接：手机打开USB调试并连接电脑，或者使用无线连接配对。",
            CreateViews(devices, null),
            null,
            scanTime);
    }

    private void ActivateTransport(AdbDeviceRecord device)
    {
        lock (_snapshotGate)
        {
            if (!string.Equals(_activeDeviceKey, device.DeviceKey, StringComparison.Ordinal) ||
                !string.Equals(_activeSerial, device.Serial, StringComparison.Ordinal))
            {
                _sessionEpoch = checked(_sessionEpoch + 1);
                _activeDeviceKey = device.DeviceKey;
                _activeSerial = device.Serial;
            }
        }
    }

    private void MarkTransportUnavailable()
    {
        lock (_snapshotGate)
        {
            if (_activeDeviceKey is { } active && _connectionPreferences.Enabled)
            {
                string physical = _devicesByKey.TryGetValue(active, out AdbDeviceRecord? route)
                    ? AndroidPhysicalDeviceIdentity.Current(route, _lastWirelessServices, _connectionPreferences.Value) : PhysicalKey(active);
                if (!_connectionPreferences.Value.Devices.Any(saved => saved.Transport == (route?.Transport ?? Snapshot.SelectedDevice?.Transport) && AndroidPhysicalDeviceIdentity.Saved(saved) == physical && saved.AutoConnect))
                {
                    _manualDeviceKey = _preferredDeviceKey = null;
                    _automaticDeviceKeys.Remove(ConnectionPreferencesStore.Scope(physical, route?.Transport ?? Snapshot.SelectedDevice?.Transport ?? AndroidTransport.Usb));
                }
            }
            _activeDeviceKey = null;
            _activeSerial = null;
            _probedDeviceKey = null;
            _selectedDetails = null;
        }
    }

    private void RememberMetadata(string key, AdbProbeResult? probe)
    {
        if (!_deviceMetadata.ContainsKey(key) && _deviceMetadata.Count >= 32) { _deviceMetadata.Remove(_deviceMetadata.Keys.First(candidate => candidate != _probedDeviceKey)); }
        _deviceMetadata[key] = (probe, Environment.TickCount64 + 30_000);
    }

    private AdbDeviceRecord WithPhysicalIdentity(AdbDeviceRecord device, string? services)
    {
        AdbProbeResult? probe = _deviceMetadata.TryGetValue(device.DeviceKey, out var entry) ? entry.Probe : null;
        AdbDeviceRecord value = device with
        {
            PhysicalDeviceKey = device.Transport == AndroidTransport.Usb ? AdbOutputParser.CreateDeviceKey(device.Serial) : probe?.PhysicalDeviceKey ?? device.PhysicalDeviceKey,
            RecognizedName = probe is null ? device.RecognizedName : AndroidDeviceIdentity.DisplayName(probe)
        };
        return value with { PhysicalDeviceKey = AndroidPhysicalDeviceIdentity.Current(value, services, _connectionPreferences.Value) };
    }

    private async ValueTask ReconcilePhysicalDevicesAsync(IReadOnlyList<AdbDeviceRecord> devices, IReadOnlyList<AdbDeviceRecord> aliases, string? services, CancellationToken cancellationToken)
    {
        AdbDeviceRecord[] resolved = devices.Select(device => WithPhysicalIdentity(device, services)).ToArray();
        lock (_snapshotGate) { _devicesByKey = resolved.ToDictionary(device => device.DeviceKey, StringComparer.Ordinal); }
        if (_connectionPreferences.Enabled) { await ReconcilePreferencesAsync([.. resolved, .. aliases.Select(device => WithPhysicalIdentity(device, services))], services, cancellationToken).ConfigureAwait(false); }
    }

    private AndroidDeviceView[] CreateViews(IReadOnlyList<AdbDeviceRecord> devices, string? selectedDeviceKey) =>
        devices.Select(device =>
        {
            AdbProbeResult? identity = _deviceMetadata.TryGetValue(device.DeviceKey, out var entry) ? entry.Probe : null;
            string physical = AndroidPhysicalDeviceIdentity.Current(WithPhysicalIdentity(device, _lastWirelessServices), _lastWirelessServices, _connectionPreferences.Value);
            string? customName = _connectionPreferences.Value.Devices.FirstOrDefault(saved => saved.Transport == device.Transport && AndroidPhysicalDeviceIdentity.Saved(saved) == physical)?.CustomName;
            return new AndroidDeviceView(device.DeviceKey,
                customName ?? OriginalDisplayName(device),
                identity is null || string.IsNullOrWhiteSpace(identity.Model) ? device.Model : identity.Model,
                device.Status, device.Transport, device.DeviceKey == selectedDeviceKey, identity?.AndroidVersion ?? "")
            { PhysicalDeviceKey = physical };
        }).ToArray();

    private void PublishFromCurrent(
        AndroidConnectionState state,
        string reasonCode,
        string message)
    {
        AndroidConnectionSnapshot current = Snapshot;
        Publish(
            state,
            reasonCode,
            message,
            current.Devices,
            current.SelectedDevice,
            current.LastSuccessfulScanAt);
    }

    private void Publish(
        AndroidConnectionState state,
        string reasonCode,
        string message,
        IReadOnlyList<AndroidDeviceView> devices,
        AndroidDeviceDetails? selectedDevice,
        DateTimeOffset? lastSuccessfulScanAt)
    {
        AndroidConnectionSnapshot next;
        lock (_snapshotGate)
        {
            if (IsEquivalent(
                    _snapshot,
                    state,
                    reasonCode,
                    message,
                    devices,
                    selectedDevice))
            {
                if (_snapshot.LastSuccessfulScanAt != lastSuccessfulScanAt)
                {
                    _snapshot = _snapshot with { LastSuccessfulScanAt = lastSuccessfulScanAt };
                }

                return;
            }

            next = new AndroidConnectionSnapshot(
                _snapshot.Revision + 1,
                state,
                reasonCode,
                message,
                devices,
                selectedDevice,
                DateTimeOffset.UtcNow,
                lastSuccessfulScanAt);
            _snapshot = next;
        }

        WriteLog(
            "state_changed",
            reasonCode,
            message,
            selectedDevice?.DeviceKey,
            state,
            devices.Count);
        StateChanged?.Invoke(this, new AndroidConnectionChangedEventArgs(next));
    }

    private static bool IsEquivalent(
        AndroidConnectionSnapshot current,
        AndroidConnectionState state,
        string reasonCode,
        string message,
        IReadOnlyList<AndroidDeviceView> devices,
        AndroidDeviceDetails? selectedDevice) =>
        current.State == state &&
        string.Equals(current.ReasonCode, reasonCode, StringComparison.Ordinal) &&
        string.Equals(current.Message, message, StringComparison.Ordinal) &&
        current.Devices.SequenceEqual(devices) &&
        current.SelectedDevice == selectedDevice;

    private void WriteLog(
        string eventName,
        string reasonCode,
        string message,
        string? deviceKey = null,
        AndroidConnectionState? state = null,
        int? deviceCount = null,
        TimeSpan? duration = null,
        string? toolVersion = null,
        string? deviceModel = null,
        string? androidVersion = null,
        int? androidSdk = null,
        string? cpuAbi = null)
    {
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            eventName,
            reasonCode,
            message,
            deviceKey,
            state,
            deviceCount,
            duration is null ? null : (long)duration.Value.TotalMilliseconds,
            toolVersion,
            deviceModel,
            androidVersion,
            androidSdk,
            cpuAbi));
    }

    private string OriginalDisplayName(AdbDeviceRecord device)
    {
        if (_deviceMetadata.TryGetValue(device.DeviceKey, out var entry) && entry.Probe is { } probe)
        { return AndroidDeviceIdentity.DisplayName(probe); }
        string physical = AndroidPhysicalDeviceIdentity.Current(WithPhysicalIdentity(device, _lastWirelessServices), _lastWirelessServices, _connectionPreferences.Value);
        lock (_snapshotGate)
        {
            string? known = _devicesByKey.Values.FirstOrDefault(route => route.PhysicalDeviceKey == physical && route.RecognizedName is not null)?.RecognizedName;
            if (known is not null) { return known; }
        }
        return _connectionPreferences.Value.Devices.FirstOrDefault(saved => AndroidPhysicalDeviceIdentity.Saved(saved) == physical)?.DisplayName ?? AndroidDeviceIdentity.UnprobedName(device.Model);
    }

    private string DisplayName(AdbDeviceRecord device) => _connectionPreferences.Value.Devices.FirstOrDefault(saved => saved.Transport == device.Transport &&
        AndroidPhysicalDeviceIdentity.Saved(saved) == AndroidPhysicalDeviceIdentity.Current(WithPhysicalIdentity(device, _lastWirelessServices), _lastWirelessServices, _connectionPreferences.Value))?.CustomName ?? OriginalDisplayName(device);

}

internal readonly record struct ResolvedAndroidDevice(
    string DeviceKey,
    string Serial,
    string DisplayName,
    long SessionEpoch);
