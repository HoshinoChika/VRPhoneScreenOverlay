namespace VRPhoneScreenOverlay.Android;

internal sealed partial class AndroidConnectionService
{
    private readonly ConnectionPreferencesStore _connectionPreferences;
    private string? _manualDeviceKey;
    private bool _automaticSelectionSuppressed;
    private long _nextManagedReconnect;
    private int _managedReconnectIndex;
    private readonly Func<CancellationToken, ValueTask<IAdbCommandRunner>> _managedRunnerFactory;
    private IAdbCommandRunner? _managedRunner;
    // Startup eligibility is bounded by the 64 saved records. Editing a preference
    // does not introduce a new connection intent into the current session.
    private readonly HashSet<string> _automaticDeviceKeys = new(StringComparer.Ordinal);
    private bool _automaticPreferencesLoaded;
    private string? _lastWirelessServices;

    private async ValueTask InitializeConnectionPreferencesAsync(CancellationToken cancellationToken)
    {
        await _connectionPreferences.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (_automaticPreferencesLoaded) { return; }
        _automaticPreferencesLoaded = true;
        _automaticDeviceKeys.UnionWith(_connectionPreferences.Value.Devices.Where(device => device.AutoConnect)
            .Select(ConnectionPreferencesStore.Scope));
    }

    private ConnectionPreferences AutomaticPreferences => _connectionPreferences.Value with
    { Devices = _connectionPreferences.Value.Devices.Where(device => _automaticDeviceKeys.Contains(ConnectionPreferencesStore.Scope(device))).ToArray() };

    public ConnectionManagementSnapshot ManagementSnapshot
    {
        get
        {
            ConnectionPreferences preferences = _connectionPreferences.Value;
            AndroidConnectionSnapshot current = Snapshot;
            ManagedAndroidDevice[] available = current.Devices.Where(device => device.Status is AndroidDeviceStatus.Ready or AndroidDeviceStatus.Unauthorized)
                .Where(device => ConnectionPreferencesStore.Supports(preferences.Method, device.Transport))
                .GroupBy(device => device.PhysicalDeviceKey ?? "route:" + device.DeviceKey).Select(group => group
                    .OrderByDescending(device => device.IsSelected).ThenByDescending(device => device.Status == AndroidDeviceStatus.Ready).First()).Select(device =>
            {
                SavedAndroidConnection? saved = preferences.Devices.Where(value => value.Transport == device.Transport && AndroidPhysicalDeviceIdentity.Saved(value) == (device.PhysicalDeviceKey ?? "route:" + device.DeviceKey))
                    .OrderByDescending(value => value.LastConnectedAt).FirstOrDefault();
                return new ManagedAndroidDevice(device.DeviceKey, saved?.CustomName ?? device.DisplayName, device.Model, device.Transport, true,
                    device.Status, device.IsSelected && current.IsReady, saved?.AutoConnect == true, saved?.LastConnectedAt, saved?.CustomName);
            }).ToArray();
            HashSet<string> detected = current.Devices.Where(device => device.Status is AndroidDeviceStatus.Ready or AndroidDeviceStatus.Unauthorized)
                .Where(device => ConnectionPreferencesStore.Supports(preferences.Method, device.Transport))
                .Select(device => device.PhysicalDeviceKey ?? "route:" + device.DeviceKey).ToHashSet(StringComparer.Ordinal);
            ManagedAndroidDevice[] history = preferences.Devices.Where(saved => ConnectionPreferencesStore.Supports(preferences.Method, saved.Transport) && saved.LastConnectedAt is not null && !detected.Contains(AndroidPhysicalDeviceIdentity.Saved(saved)))
                .GroupBy(AndroidPhysicalDeviceIdentity.Saved).Select(group => group.OrderByDescending(saved => saved.LastConnectedAt).First())
                .OrderByDescending(saved => saved.LastConnectedAt).Select(saved => new ManagedAndroidDevice(saved.DeviceKey,
                    saved.CustomName ?? saved.DisplayName, saved.Model, saved.Transport, false, AndroidDeviceStatus.Offline, false,
                    saved.AutoConnect, saved.LastConnectedAt, saved.CustomName)).ToArray();
            return new(preferences.Method, [.. available, .. history]);
        }
    }

    public bool IsSelectedDevice(string deviceKey) => Snapshot.SelectedDevice is { } selected &&
        PhysicalKey(selected.DeviceKey) == PhysicalKey(deviceKey) &&
        selected.Transport == PreferenceForRoute(deviceKey)?.Transport;

    public ValueTask SetConnectionMethodAsync(PhoneConnectionMethod method, CancellationToken cancellationToken) =>
        SetConnectionMethodAsync(method, preserveCurrentConnection: false, cancellationToken);

    public async ValueTask SetConnectionMethodAsync(PhoneConnectionMethod method, bool preserveCurrentConnection,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(method)) { throw new ArgumentOutOfRangeException(nameof(method)); }
        await ChangePreferencesAsync(async () =>
        {
            await _connectionPreferences.SaveAsync(_connectionPreferences.Value with { Method = method }, cancellationToken).ConfigureAwait(false);
            if (!preserveCurrentConnection) { ClearManagedSelection(); }
            _automaticSelectionSuppressed = false;
            _automaticDeviceKeys.UnionWith(_connectionPreferences.Value.Devices
                .Where(device => device.AutoConnect)
                .Select(ConnectionPreferencesStore.Scope));
        }, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SetDeviceAutoConnectAsync(string deviceKey, bool enabled, CancellationToken cancellationToken)
    {
        await ChangePreferencesAsync(async () =>
        {
            ConnectionPreferences preferences = _connectionPreferences.Value;
            SavedAndroidConnection? saved = PreferenceForRoute(deviceKey);
            if (saved is null) { throw new AndroidConnectionException("CONNECTION_DEVICE_UNKNOWN", "设备已不在列表中，请刷新设备"); }
            saved = saved with { PhysicalDeviceKey = PhysicalKey(deviceKey), AutoConnect = enabled, PreferenceChangedAt = DateTimeOffset.UtcNow };
            string physical = AndroidPhysicalDeviceIdentity.Saved(saved);
            await _connectionPreferences.SaveAsync(preferences with
            {
                Devices = [.. preferences.Devices.Where(device => device.DeviceKey != deviceKey).Select(device => device.Transport == saved.Transport && AndroidPhysicalDeviceIdentity.Saved(device) == physical
                ? device with { AutoConnect = enabled, PreferenceChangedAt = saved.PreferenceChangedAt } : device), saved]
            }, cancellationToken).ConfigureAwait(false);
            if (!enabled) { _automaticDeviceKeys.Remove(ConnectionPreferencesStore.Scope(saved)); }
            else if (_activeDeviceKey is { } active && PhysicalKey(active) == physical && Snapshot.SelectedDevice?.Transport == saved.Transport)
            { _automaticDeviceKeys.Add(ConnectionPreferencesStore.Scope(saved)); }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SaveDevicePreferencesAsync(string deviceKey, string? customName, bool autoConnect, CancellationToken cancellationToken)
    {
        if (customName is not null && !DeviceNamePolicy.IsValid(customName))
        { throw new AndroidConnectionException("CONNECTION_NAME_INVALID", "名称限 1–20 个汉字、英文字母或数字"); }
        await ChangePreferencesAsync(async () =>
        {
            SavedAndroidConnection saved = PreferenceForRoute(deviceKey) ??
                throw new AndroidConnectionException("CONNECTION_DEVICE_UNKNOWN", "设备已不在列表中，请刷新设备");
            string physical = PhysicalKey(deviceKey);
            DateTimeOffset changed = DateTimeOffset.UtcNow;
            saved = saved with { PhysicalDeviceKey = physical, CustomName = customName, AutoConnect = autoConnect, PreferenceChangedAt = changed };
            await _connectionPreferences.SaveAsync(_connectionPreferences.Value with
            {
                Devices = [.. _connectionPreferences.Value.Devices.Where(device => device.DeviceKey != deviceKey).Select(device =>
                    device.Transport == saved.Transport && AndroidPhysicalDeviceIdentity.Saved(device) == physical ? device with { CustomName = customName, AutoConnect = autoConnect, PreferenceChangedAt = changed } : device), saved]
            }, cancellationToken).ConfigureAwait(false);
            if (!autoConnect) { _automaticDeviceKeys.Remove(ConnectionPreferencesStore.Scope(saved)); }
            else if (_activeDeviceKey is { } active && PhysicalKey(active) == physical && Snapshot.SelectedDevice?.Transport == saved.Transport)
            { _automaticDeviceKeys.Add(ConnectionPreferencesStore.Scope(saved)); }
            AndroidConnectionSnapshot current = Snapshot;
            AdbDeviceRecord[] routes;
            lock (_snapshotGate) { routes = _devicesByKey.Values.ToArray(); }
            Publish(current.State, current.ReasonCode, current.Message, CreateViews(routes, current.SelectedDevice?.DeviceKey), current.SelectedDevice, current.LastSuccessfulScanAt);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RenameDeviceAsync(string deviceKey, string name, CancellationToken cancellationToken)
    {
        if (!DeviceNamePolicy.IsValid(name))
        { throw new AndroidConnectionException("CONNECTION_NAME_INVALID", "名称限 1–20 个汉字、英文字母或数字"); }
        await ChangePreferencesAsync(async () =>
        {
            ConnectionPreferences preferences = _connectionPreferences.Value;
            SavedAndroidConnection? saved = PreferenceForRoute(deviceKey);
            if (saved is null) { throw new AndroidConnectionException("CONNECTION_DEVICE_UNKNOWN", "设备已不在列表中，请刷新设备"); }
            saved = saved with { PhysicalDeviceKey = PhysicalKey(deviceKey), CustomName = name, PreferenceChangedAt = DateTimeOffset.UtcNow };
            string physical = AndroidPhysicalDeviceIdentity.Saved(saved);
            await _connectionPreferences.SaveAsync(preferences with
            {
                Devices = [.. preferences.Devices.Where(device => device.DeviceKey != deviceKey).Select(device => device.Transport == saved.Transport && AndroidPhysicalDeviceIdentity.Saved(device) == physical
                ? device with { CustomName = name, PreferenceChangedAt = saved.PreferenceChangedAt } : device), saved]
            }, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ForgetDeviceAsync(string deviceKey, CancellationToken cancellationToken)
    {
        await ChangePreferencesAsync(async () =>
        {
            SavedAndroidConnection? saved = PreferenceForRoute(deviceKey);
            if (saved is null) { return; }
            string scope = ConnectionPreferencesStore.Scope(saved);
            bool selected = IsSelectedDevice(deviceKey);
            await _connectionPreferences.SaveAsync(_connectionPreferences.Value with
            { Devices = _connectionPreferences.Value.Devices.Where(device => ConnectionPreferencesStore.Scope(device) != scope).ToArray() }, cancellationToken).ConfigureAwait(false);
            _automaticDeviceKeys.Remove(scope);
            if (selected || _preferredDeviceKey == deviceKey || _manualDeviceKey == deviceKey) { ClearManagedSelection(); }
        }, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisconnectSelectionAsync(CancellationToken cancellationToken)
    {
        await ChangePreferencesAsync(() =>
        {
            ClearManagedSelection();
            _automaticSelectionSuppressed = true;
            return ValueTask.CompletedTask;
        }, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ConnectKnownDeviceAsync(string deviceKey, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopSource.Token);
        cancellationToken = linked.Token;
        SavedAndroidConnection? saved = _connectionPreferences.Value.Devices.FirstOrDefault(value => value.DeviceKey == deviceKey);
        ManagedAndroidDevice? device = ManagementSnapshot.Devices.FirstOrDefault(value => value.DeviceKey == deviceKey) ??
            ManagementSnapshot.Devices.FirstOrDefault(value => value.IsAvailable && PhysicalKey(value.DeviceKey) == PhysicalKey(deviceKey)) ??
            (saved is null ? null : new(saved.DeviceKey, saved.CustomName ?? saved.DisplayName, saved.Model, saved.Transport,
                false, AndroidDeviceStatus.Offline, false, saved.AutoConnect, saved.LastConnectedAt, saved.CustomName));
        if (device is null) { throw new AndroidConnectionException("CONNECTION_DEVICE_UNKNOWN", "设备已不在列表中，请刷新设备"); }
        if (!ConnectionPreferencesStore.Supports(_connectionPreferences.Value.Method, device.Transport))
        { throw new AndroidConnectionException("CONNECTION_METHOD_MISMATCH", "请先选择与此设备对应的 USB 或无线连接方式"); }
        if (device.IsAvailable)
        {
            if (device.Status != AndroidDeviceStatus.Ready)
            { throw new AndroidConnectionException("CONNECTION_DEVICE_NOT_READY", "请先在手机上授权调试连接"); }
            await SelectDeviceAsync(device.DeviceKey, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (saved is not { Transport: AndroidTransport.Network })
        { throw new AndroidConnectionException("CONNECTION_DEVICE_NOT_FOUND", "未发现设备，请插入 USB 数据线并允许调试后刷新设备"); }
        IAdbCommandRunner runner = _managedRunner ??= await _managedRunnerFactory(cancellationToken).ConfigureAwait(false);
        AdbCommandResult mdns = await runner.RunAsync(["mdns", "services"], TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        string? endpoint = ConnectionPreferencesStore.ResolveEndpoint(saved.ServiceName, mdns.Succeeded ? mdns.StandardOutput : null) ?? saved.Endpoint;
        if (endpoint is null || !WirelessAdbEndpoint.TryNormalize(endpoint, out _))
        { throw new AndroidConnectionException("CONNECTION_DEVICE_NOT_FOUND", "未发现无线设备，请重新开启无线调试并配对"); }
        AndroidOperationResult result = await ExecuteWirelessAsync(WirelessAdbOperation.Connect, endpoint, "", cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) { throw new AndroidConnectionException(result.ReasonCode, result.Message); }
        string endpointKey = AdbOutputParser.CreateDeviceKey(endpoint);
        string? serviceName = WirelessAdbEndpoint.FindConnectService(mdns.StandardOutput, endpoint);
        string? selectedKey = Snapshot.Devices.FirstOrDefault(value => value.DeviceKey == deviceKey || value.DeviceKey == endpointKey ||
            (serviceName is not null && value.DeviceKey == AdbOutputParser.CreateDeviceKey(serviceName)))?.DeviceKey;
        if (selectedKey is null) { throw new AndroidConnectionException("CONNECTION_DEVICE_NOT_FOUND", "无线连接尚未就绪，请刷新设备后重试"); }
        await SelectDeviceAsync(selectedKey, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ChangePreferencesAsync(Func<ValueTask> change, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InitializeConnectionPreferencesAsync(cancellationToken).ConfigureAwait(false);
            await change().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new AndroidConnectionException("CONNECTION_PREFERENCES_SAVE_FAILED", "连接偏好保存失败，请稍后重试"); }
        finally { _operationGate.Release(); }
    }

    private void ClearManagedSelection()
    {
        _manualDeviceKey = _preferredDeviceKey = _probedDeviceKey = null;
        _selectedDetails = null;
        MarkTransportUnavailable();
    }

    private string PhysicalKey(string key) => Snapshot.Devices.FirstOrDefault(device => device.DeviceKey == key)?.PhysicalDeviceKey ??
        _connectionPreferences.Value.Devices.Where(device => device.DeviceKey == key).Select(AndroidPhysicalDeviceIdentity.Saved).FirstOrDefault() ?? "route:" + key;

    private SavedAndroidConnection? PreferenceForRoute(string key)
    {
        ConnectionPreferences preferences = _connectionPreferences.Value;
        SavedAndroidConnection? exact = preferences.Devices.FirstOrDefault(device => device.DeviceKey == key);
        if (exact is not null) { return exact; }
        AndroidDeviceView? available = Snapshot.Devices.FirstOrDefault(device => device.DeviceKey == key);
        if (available is null) { return null; }
        string physical = PhysicalKey(key);
        SavedAndroidConnection? shared = preferences.Devices.Where(device => device.Transport == available.Transport && AndroidPhysicalDeviceIdentity.Saved(device) == physical)
            .OrderByDescending(device => device.PreferenceChangedAt).ThenByDescending(device => device.LastConnectedAt).FirstOrDefault();
        return new(key, available.DisplayName, available.Model, available.Transport, shared?.AutoConnect == true, null,
            CustomName: shared?.CustomName, PhysicalDeviceKey: physical, PreferenceChangedAt: shared?.PreferenceChangedAt);
    }

    private async ValueTask ReconcilePreferencesAsync(IReadOnlyList<AdbDeviceRecord> routes, string? services,
        CancellationToken cancellationToken)
    {
        // Firmware discovery can replace a route identity with a verified physical
        // identity. Carry existing eligibility through that migration only; a save
        // of a newly enabled preference must still not initiate a connection.
        HashSet<string> eligibleRoutes = _connectionPreferences.Value.Devices
            .Where(saved => _automaticDeviceKeys.Contains(ConnectionPreferencesStore.Scope(saved)))
            .Select(saved => saved.DeviceKey).ToHashSet(StringComparer.Ordinal);
        await _connectionPreferences.ReconcileAsync(routes, services, cancellationToken).ConfigureAwait(false);
        _automaticDeviceKeys.UnionWith(_connectionPreferences.Value.Devices
            .Where(saved => saved.AutoConnect && eligibleRoutes.Contains(saved.DeviceKey)).Select(ConnectionPreferencesStore.Scope));
    }

    // Discovery owns one retry every ten seconds, with no queued attempts.
    // Only explicitly enabled, previously successful endpoints/services are retried.
    private async ValueTask<bool> TryManagedReconnectAsync(AdbListResult list, CancellationToken cancellationToken)
    {
        ConnectionPreferences preferences = AutomaticPreferences;
        if (_automaticSelectionSuppressed || preferences.Method == PhoneConnectionMethod.Usb ||
            list.Devices.Any(device => device.Status == AndroidDeviceStatus.Ready &&
                (device.DeviceKey == _manualDeviceKey || device.DeviceKey == _activeDeviceKey)) ||
            ConnectionPreferencesStore.SelectAutomatic(list.Devices, preferences) is not null) { return false; }
        SavedAndroidConnection[] candidates = preferences.Devices.Where(device => device.AutoConnect && device.LastConnectedAt is not null &&
            device.Transport == AndroidTransport.Network).OrderByDescending(device => device.LastConnectedAt).ToArray();
        if (candidates.Length == 0 || Environment.TickCount64 < _nextManagedReconnect) { return false; }
        _nextManagedReconnect = Environment.TickCount64 + 10000;
        SavedAndroidConnection candidate = candidates[_managedReconnectIndex % candidates.Length];
        _managedReconnectIndex = (_managedReconnectIndex + 1) % candidates.Length;
        try
        {
            IAdbCommandRunner runner = _managedRunner ??= await _managedRunnerFactory(cancellationToken).ConfigureAwait(false);
            AdbCommandResult mdns = await runner.RunAsync(["mdns", "services"], TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            string? endpoint = ConnectionPreferencesStore.ResolveEndpoint(candidate.ServiceName, mdns.Succeeded ? mdns.StandardOutput : null) ?? candidate.Endpoint;
            if (endpoint is null || !WirelessAdbEndpoint.TryNormalize(endpoint, out string normalized)) { return false; }
            AdbCommandResult result = await runner.RunAsync(["connect", normalized], TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            return result.Succeeded;
        }
        catch (Exception exception) when (exception is AndroidConnectionException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return false; }
    }

    private AdbDeviceRecord[] CanonicalizeManagedConnections(IReadOnlyList<AdbDeviceRecord> devices, string? services) =>
        devices.Select(device =>
        {
            if (device.Transport != AndroidTransport.Network) { return device; }
            string? service = WirelessAdbEndpoint.TryNormalize(device.Serial, out string endpoint)
                ? services is null ? null : WirelessAdbEndpoint.FindConnectService(services, endpoint) : device.Serial.TrimEnd('.');
            SavedAndroidConnection? saved = _connectionPreferences.Value.Devices.FirstOrDefault(value =>
                service is not null && value.ServiceName == service);
            return saved is null ? device : device with { DeviceKey = saved.DeviceKey };
        }).DistinctBy(device => device.DeviceKey).ToArray();
}
