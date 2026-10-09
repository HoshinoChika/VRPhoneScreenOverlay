using System.Text.Json;
using Valve.VR;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ConnectionManagementDeviceTests
{
    [DeviceFact]
    [Trait("Category", "Device")]
    [Trait("Feature", "connections")]
    public async Task SaveOptOutThenOptInAndExplicitConnectReopensRealOverlayAfterPreviousSessionClosed()
    {
        string model = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_MODEL")!;
        string resources = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_ADB_DIRECTORY")!;
        string output = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_OUTPUT")!;
        Directory.CreateDirectory(output);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(110));
        AdbClient adb = new(new AdbCommandRunner(Path.Combine(resources, "adb.exe"), killProcessTree: false), TimeSpan.FromSeconds(10));
        await using AndroidConnectionService phone = new(resources, NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions { ConnectionPreferencesPath = Path.Combine(output, "reopen-preferences.json") }, new LeaveServerRunning());
        phone.SetClientForTest(new TargetClient(adb, model));
        for (int scan = 0; scan < 4; scan++) { await phone.ScanOnceForTestAsync(deadline.Token); }
        using OverlayRuntime runtime = new();
        AndroidSessionFactories factories = AndroidConnectionFactory.CreateDefaultSessionFactories(phone, NullAndroidConnectionLog.Instance);
        await using PhoneControlService control = new(factories.Control, NullAndroidConnectionLog.Instance);
        await using PhoneAudioService audio = new(factories.Audio, NullAndroidConnectionLog.Instance);
        await using PhoneOverlayService overlay = new(factories.Video, control,
            AndroidConnectionFactory.CreateDefaultKeyguardStateService(phone), NullAndroidConnectionLog.Instance);
        await using PhoneMediaSessionCoordinator media = new(overlay, audio, control,
            AndroidConnectionFactory.CreateDefaultMediaPlaybackControl(phone, NullAndroidConnectionLog.Instance), phone, () => OpenVrRuntimeHost.CurrentSystem is not null);
        PhoneConnectionManagementService application = new(phone, phone, media, () => new(), () => true);
        media.ConfigureAutoStart(true, new());
        string usb = Assert.Single(phone.ManagementSnapshot.Devices).DeviceKey;
        await phone.SaveDevicePreferencesAsync(usb, "隔离测试平板", true, deadline.Token);
        await application.ConnectAsync(usb, deadline.Token);
        await WaitFrames();
        long firstFrames = overlay.Snapshot.SubmittedFrames;
        await phone.SaveDevicePreferencesAsync(usb, "隔离测试平板", false, deadline.Token);
        Assert.Equal(PhoneMediaSessionState.Running, media.Snapshot.State);
        await application.ChangeMethodAsync(PhoneConnectionMethod.WirelessScan, deadline.Token);
        Assert.Equal(PhoneOverlayState.Stopped, overlay.Snapshot.State);
        Assert.False(phone.Snapshot.IsReady);
        await phone.SetConnectionMethodAsync(PhoneConnectionMethod.Usb, deadline.Token);
        Assert.False(phone.Snapshot.IsReady);
        await phone.SaveDevicePreferencesAsync(usb, "隔离测试平板", true, deadline.Token);
        Assert.False(phone.Snapshot.IsReady); // Saving alone never connects/opens.
        Assert.Equal(PhoneOverlayState.Stopped, overlay.Snapshot.State);
        await application.ConnectAsync(usb, deadline.Token);
        await WaitFrames();
        Assert.Equal(PhoneMediaSessionState.Running, media.Snapshot.State);
        Assert.Equal(PhoneControlState.Ready, control.Snapshot.State);
        await File.WriteAllTextAsync(Path.Combine(output, "reopen-result.json"), JsonSerializer.Serialize(new
        { firstFrames, reopenedFrames = overlay.Snapshot.SubmittedFrames, overlay.Snapshot.Width, overlay.Snapshot.Height, savedWithoutConnecting = true, reopenedAfterPriorClose = true }));

        async Task WaitFrames()
        {
            while (overlay.Snapshot.State != PhoneOverlayState.Running || overlay.Snapshot.SubmittedFrames < 3)
            { await Task.Delay(100, deadline.Token); }
        }
    }

    [DeviceFact]
    [Trait("Category", "Device")]
    [Trait("Feature", "connections")]
    public async Task ConnectedProvidedDeviceConfigurationSaveDoesNotDisconnectAndSwitchDoesNotReopenOptOut()
    {
        string model = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_MODEL")!;
        string resources = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_ADB_DIRECTORY")!;
        string output = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_OUTPUT")!;
        Directory.CreateDirectory(output);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(45));
        AdbClient adb = new(new AdbCommandRunner(Path.Combine(resources, "adb.exe"), killProcessTree: false), TimeSpan.FromSeconds(10));
        await using AndroidConnectionService phone = new(resources, NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions { ConnectionPreferencesPath = Path.Combine(output, "save-preferences.json") }, new LeaveServerRunning());
        phone.SetClientForTest(new TargetClient(adb, model));
        for (int scan = 0; scan < 4; scan++) { await phone.ScanOnceForTestAsync(timeout.Token); }
        string usb = Assert.Single(phone.ManagementSnapshot.Devices).DeviceKey;
        await phone.SelectDeviceAsync(usb, timeout.Token);
        Assert.True(phone.TryResolveReadyDevice(null, out var before));
        await phone.SaveDevicePreferencesAsync(usb, "已连接测试平板", false, timeout.Token);
        Assert.True(phone.TryResolveReadyDevice(null, out var after));
        Assert.Equal(before.SessionEpoch, after.SessionEpoch);
        Assert.Equal(before.Serial, after.Serial);
        Assert.Equal("已连接测试平板", Assert.Single(phone.ManagementSnapshot.Devices).DisplayName);
        await phone.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessScan, timeout.Token);
        Assert.False(phone.Snapshot.IsReady);
        for (int scan = 0; scan < 3; scan++) { await phone.ScanOnceForTestAsync(timeout.Token); }
        Assert.False(phone.Snapshot.IsReady);
        ManagedAndroidDevice wireless = Assert.Single(phone.ManagementSnapshot.Devices);
        Assert.Equal("已连接测试平板", wireless.DisplayName);
        await phone.SelectDeviceAsync(wireless.DeviceKey, timeout.Token);
        Assert.True(phone.Snapshot.IsReady);
        await File.WriteAllTextAsync(Path.Combine(output, "save-result.json"), JsonSerializer.Serialize(new
        { connectedSaveKeptLease = true, savedName = true, switchRequiredManualConnect = true, explicitConnectSucceeded = true, userPreferencesModified = false }));
    }

    [DeviceFact]
    [Trait("Category", "Device")]
    [Trait("Feature", "connections")]
    public async Task ExistingProvidedDeviceHistoryHasNoDuplicatePhysicalRows()
    {
        string model = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_MODEL")!;
        string resources = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_ADB_DIRECTORY")!;
        string output = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_OUTPUT")!;
        string legacy = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_LEGACY") ?? throw new InvalidOperationException("Explicit legacy preferences required.");
        Directory.CreateDirectory(output);
        ConnectionPreferences? saved = JsonSerializer.Deserialize<ConnectionPreferences>(await File.ReadAllTextAsync(legacy));
        Assert.NotNull(saved);
        string copy = Path.Combine(output, "legacy-copy.json");
        var records = saved.Devices.Where(device => device.Model == model).ToArray();
        Assert.True(records.Length > 1, "The reproduction requires the actual legacy multi-route records.");
        await File.WriteAllTextAsync(copy, JsonSerializer.Serialize(saved with { Devices = records }));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(45));
        AdbClient adb = new(new AdbCommandRunner(Path.Combine(resources, "adb.exe"), killProcessTree: false), TimeSpan.FromSeconds(10));
        await using AndroidConnectionService phone = new(resources, NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions { ConnectionPreferencesPath = copy }, new LeaveServerRunning());
        phone.SetClientForTest(new TargetClient(adb, model));
        for (int scan = 0; scan < 5; scan++) { await phone.ScanOnceForTestAsync(timeout.Token); }
        foreach (PhoneConnectionMethod method in new[] { PhoneConnectionMethod.Usb, PhoneConnectionMethod.WirelessScan, PhoneConnectionMethod.Usb })
        {
            await phone.SetConnectionMethodAsync(method, timeout.Token);
            if (!phone.Snapshot.IsReady) { await phone.SelectDeviceAsync(Assert.Single(phone.ManagementSnapshot.Devices).DeviceKey, timeout.Token); }
            ManagedAndroidDevice current = Assert.Single(phone.ManagementSnapshot.Devices);
            Assert.True(current.IsAvailable);
            Assert.True(current.IsSelected);
            Assert.False(current.DisplayName.StartsWith("ID ", StringComparison.Ordinal));
        }
        await File.WriteAllTextAsync(Path.Combine(output, "legacy-result.json"), JsonSerializer.Serialize(new
        { legacyRoutes = records.Length, currentDevices = 1, historyDuplicates = 0, allTransportSwitches = "passed" }));
    }

    [DeviceFact]
    [Trait("Category", "Device")]
    [Trait("Feature", "connections")]
    public async Task SwitchingTransportStartsAndRestartsRealVrOverlayWithSubmittedFrames()
    {
        string model = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_MODEL") ?? throw new InvalidOperationException("Explicit target required.");
        string resources = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_ADB_DIRECTORY") ?? throw new InvalidOperationException("Explicit resources required.");
        string output = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_OUTPUT") ?? throw new InvalidOperationException("Explicit ignored output required.");
        Directory.CreateDirectory(output);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(110));
        await using OverlayEvidenceLog log = new(Path.Combine(output, "overlay-phases.json"));
        AdbClient adb = new(new AdbCommandRunner(Path.Combine(resources, "adb.exe"), killProcessTree: false), TimeSpan.FromSeconds(10));
        await using AndroidConnectionService phone = new(resources, NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions { ConnectionPreferencesPath = Path.Combine(output, "overlay-connections.json") }, new LeaveServerRunning());
        phone.SetClientForTest(new TargetClient(adb, model));
        await phone.ScanOnceForTestAsync(deadline.Token);
        await phone.ScanOnceForTestAsync(deadline.Token);
        using OverlayRuntime runtime = new();
        AndroidSessionFactories factories = AndroidConnectionFactory.CreateDefaultSessionFactories(phone, log);
        await using PhoneControlService control = new(factories.Control, log);
        await using PhoneAudioService audio = new(factories.Audio, log);
        await using PhoneOverlayService overlay = new(factories.Video, control,
            AndroidConnectionFactory.CreateDefaultKeyguardStateService(phone), log);
        await using PhoneMediaSessionCoordinator media = new(overlay, audio, control,
            AndroidConnectionFactory.CreateDefaultMediaPlaybackControl(phone, NullAndroidConnectionLog.Instance), phone, () => OpenVrRuntimeHost.CurrentSystem is not null);
        PhoneConnectionManagementService application = new(phone, phone, media, () => new AndroidVideoOptions());
        List<object> evidence = [];
        foreach (PhoneConnectionMethod method in new[] { PhoneConnectionMethod.Usb, PhoneConnectionMethod.WirelessScan, PhoneConnectionMethod.Usb })
        {
            await application.ChangeMethodAsync(method, deadline.Token);
            await phone.SetDeviceAutoConnectAsync(phone.Snapshot.SelectedDevice!.DeviceKey, true, deadline.Token);
            while (overlay.Snapshot.State != PhoneOverlayState.Running || overlay.Snapshot.SubmittedFrames < 3)
            {
                if (overlay.Snapshot.State == PhoneOverlayState.Faulted)
                { throw new InvalidOperationException("Real overlay failed: " + overlay.Snapshot.ReasonCode); }
                await Task.Delay(100, deadline.Token);
            }
            Assert.Equal(PhoneMediaSessionState.Running, media.Snapshot.State);
            Assert.Equal(method == PhoneConnectionMethod.Usb ? AndroidTransport.Usb : AndroidTransport.Network, phone.Snapshot.SelectedDevice?.Transport);
            Assert.Single(phone.Snapshot.Devices, device => device.IsSelected);
            evidence.Add(new
            {
                method,
                state = overlay.Snapshot.State,
                overlay.Snapshot.SubmittedFrames,
                overlay.Snapshot.Width,
                overlay.Snapshot.Height,
                control = control.Snapshot.State,
                audio = audio.Snapshot.State
            });
        }
        await File.WriteAllTextAsync(Path.Combine(output, "overlay-switch-result.json"), JsonSerializer.Serialize(evidence), deadline.Token);
    }

    [DeviceFact]
    [Trait("Category", "Device")]
    [Trait("Feature", "connections")]
    public async Task SuppliedDeviceSupportsUsbAndWirelessWithOneExplicitSelection()
    {
        string targetModel = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_MODEL") ??
            throw new InvalidOperationException("An explicit supplied-device model is required.");
        string resources = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_ADB_DIRECTORY") ??
            throw new InvalidOperationException("An explicit bundled ADB directory is required.");
        string output = Environment.GetEnvironmentVariable("VRPSO_CONNECTION_TEST_OUTPUT") ??
            throw new InvalidOperationException("An ignored local output directory is required.");
        Directory.CreateDirectory(output);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(45));
        AdbClient adb = new(new AdbCommandRunner(Path.Combine(resources, "adb.exe"), killProcessTree: false), TimeSpan.FromSeconds(10));
        TargetClient client = new(adb, targetModel);
        await using AndroidConnectionService service = new(resources, NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions { ConnectionPreferencesPath = Path.Combine(output, "isolated-connections.json") }, new LeaveServerRunning());
        service.SetClientForTest(client);
        for (int scan = 0; scan < 4; scan++) { await service.ScanOnceForTestAsync(deadline.Token); }
        var usb = Assert.Single(service.Snapshot.Devices, device => device.Transport == AndroidTransport.Usb && device.Status == AndroidDeviceStatus.Ready);
        var wireless = service.Snapshot.Devices.First(device => device.Transport == AndroidTransport.Network && device.Status == AndroidDeviceStatus.Ready);
        Assert.False(usb.DisplayName.StartsWith("ID ", StringComparison.Ordinal));
        Assert.Equal(usb.DisplayName, wireless.DisplayName);
        Assert.False(service.Snapshot.IsReady);
        Assert.Single(service.ManagementSnapshot.Devices);
        await service.SetDeviceAutoConnectAsync(usb.DeviceKey, true, deadline.Token);
        await service.ScanOnceForTestAsync(deadline.Token);
        Assert.False(service.Snapshot.IsReady); // The switch is a configuration, not a connection command.
        await service.SelectDeviceAsync(usb.DeviceKey, deadline.Token);
        Assert.Equal(AndroidTransport.Usb, service.Snapshot.SelectedDevice?.Transport);
        Assert.Single(service.Snapshot.Devices, device => device.IsSelected);
        Assert.True(service.TryResolveReadyDevice(null, out var wiredLease));
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessScan, deadline.Token);
        Assert.True(service.Snapshot.IsReady);
        Assert.Equal(AndroidTransport.Network, service.Snapshot.SelectedDevice?.Transport);
        Assert.Single(service.Snapshot.Devices, device => device.IsSelected);
        Assert.True(service.TryResolveReadyDevice(null, out var wirelessLease));
        Assert.Single(service.ManagementSnapshot.Devices);
        await service.RenameDeviceAsync(service.Snapshot.SelectedDevice!.DeviceKey, "隔离测试平板", deadline.Token);
        Assert.True(wirelessLease.SessionEpoch > wiredLease.SessionEpoch);
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.Usb, deadline.Token);
        Assert.True(service.Snapshot.IsReady);
        Assert.Equal(AndroidTransport.Usb, service.Snapshot.SelectedDevice?.Transport);
        Assert.Equal("隔离测试平板", Assert.Single(service.ManagementSnapshot.Devices).DisplayName);
        Assert.True(Assert.Single(service.ManagementSnapshot.Devices).AutoConnect);
        await service.ForgetDeviceAsync(service.Snapshot.SelectedDevice!.DeviceKey, deadline.Token);
        var fresh = Assert.Single(service.ManagementSnapshot.Devices);
        Assert.False(fresh.AutoConnect);
        Assert.Null(fresh.LastConnectedAt);
        Assert.Equal(usb.DisplayName, fresh.DisplayName);
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            firmwareName = usb.DisplayName,
            usb = "passed",
            wireless = "passed",
            singleSelection = "passed",
            autoConnectToggleIsConfigurationOnly = "passed",
            usbWirelessUsbSwitch = "passed",
            onePhysicalRowAndNoDuplicateHistory = "passed",
            sharedRenameAndForget = "passed",
        }), deadline.Token);
    }

    private sealed class TargetClient(IAdbClient adb, string model) : IAdbClient
    {
        private readonly HashSet<string> _allowedSerials = new(StringComparer.Ordinal);
        public async ValueTask<AdbListResult> ListDevicesAsync(CancellationToken cancellationToken)
        {
            AdbListResult all = await adb.ListDevicesAsync(cancellationToken);
            var devices = all.Devices.Where(device => device.Model == model).ToArray();
            _allowedSerials.UnionWith(devices.Select(device => device.Serial));
            return all with { Devices = devices };
        }
        public ValueTask<AdbProbeResult> ProbeAsync(string serial, CancellationToken cancellationToken)
        {
            if (!_allowedSerials.Contains(serial)) { throw new InvalidOperationException("Non-target probe refused."); }
            return adb.ProbeAsync(serial, cancellationToken);
        }
        public ValueTask<string?> ResolveWirelessServiceAsync(string endpoint, CancellationToken cancellationToken) =>
            adb.ResolveWirelessServiceAsync(endpoint, cancellationToken);
    }

    private sealed class LeaveServerRunning : IAdbServerShutdown
    {
        public ValueTask<AdbServerShutdownResult> StopAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AdbServerShutdownResult(true, "DEVICE_TEST_SERVER_PRESERVED", "测试结束后保留其他 ADB 使用者"));
    }

    // The transport test owns the overlay runtime; it does not exercise playspace input.
    private sealed class OverlayRuntime : IDisposable
    {
        public OverlayRuntime()
        {
            EVRInitError error = EVRInitError.None;
            _ = OpenVrRuntimeHost.GetOrStart(ref error);
            if (error != EVRInitError.None) { throw new InvalidOperationException("OpenVR initialization failed: " + error); }
            EVRInputError input = OpenVrRuntimeHost.EnsureActionManifestSubmitted();
            if (input is not (EVRInputError.None or EVRInputError.IPCError))
            { OpenVrRuntimeHost.Shutdown(); throw new InvalidOperationException("OpenVR input preparation failed: " + input); }
        }

        public void Dispose() => OpenVrRuntimeHost.Shutdown();
    }

    private sealed class OverlayEvidenceLog(string path) : IAndroidConnectionLogSink
    {
        // Test-owned capacity 128; drop oldest; retain reason codes and counters only.
        private readonly Queue<object> _entries = new();
        public string? CurrentLogPath => null;
        public bool TryWrite(AndroidConnectionLogEntry entry)
        {
            lock (_entries)
            {
                if (_entries.Count == 128) { _entries.Dequeue(); }
                _entries.Enqueue(new { entry.EventName, entry.ReasonCode, entry.PacketCount, entry.VideoWidth, entry.VideoHeight, entry.ExceptionType });
            }
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            object[] values;
            lock (_entries) { values = _entries.ToArray(); }
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(values));
        }
    }
}
