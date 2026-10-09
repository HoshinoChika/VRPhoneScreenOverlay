using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidConnectionServiceTests
{
    [Fact]
    public async Task EnablingAutomaticHistoryIsConfigurationOnlyUntilNextStartupOrManualConnect()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"auto-config-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "connections.json");
            var device = Assert.Single(AdbOutputParser.ParseDevices("one device usb:1 model:Phone"));
            ConnectionPreferencesStore store = new(path);
            await store.SaveAsync(new(1, PhoneConnectionMethod.Usb,
                [new(device.DeviceKey, "Phone", "Phone", AndroidTransport.Usb, false, DateTimeOffset.UtcNow.AddDays(-1))]), CancellationToken.None);
            FakeAdbClient client = new([device]);
            await using (AndroidConnectionService service = CreateManagedService(client, path))
            {
                await service.ScanOnceForTestAsync(CancellationToken.None);
                await service.SetDeviceAutoConnectAsync(device.DeviceKey, true, CancellationToken.None);
                await service.ScanOnceForTestAsync(CancellationToken.None);
                Assert.False(service.Snapshot.IsReady);
            }
            await using AndroidConnectionService restarted = CreateManagedService(client, path);
            await restarted.ScanOnceForTestAsync(CancellationToken.None);
            Assert.True(restarted.Snapshot.IsReady);
            await restarted.SetDeviceAutoConnectAsync(device.DeviceKey, false, CancellationToken.None);
            await restarted.ScanOnceForTestAsync(CancellationToken.None);
            Assert.True(restarted.Snapshot.IsReady); // Saving a preference does not disconnect the current device either.
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RenamePersistsAcrossRefreshAndRestartWithoutChangingIdentityRecencyOrAutomaticPreference()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"connection-name-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "connections.json");
            var device = Assert.Single(AdbOutputParser.ParseDevices("one device usb:1 model:Phone"));
            FakeAdbClient client = new([device]);
            await using (AndroidConnectionService service = CreateManagedService(client, path))
            {
                await service.ScanOnceForTestAsync(CancellationToken.None);
                await service.SetDeviceAutoConnectAsync(device.DeviceKey, true, CancellationToken.None);
                Assert.False(service.Snapshot.IsReady);
                await service.ScanOnceForTestAsync(CancellationToken.None);
                Assert.False(service.Snapshot.IsReady);
                await service.SelectDeviceAsync(device.DeviceKey, CancellationToken.None);
                Assert.True(service.TryResolveReadyDevice(null, out var original));
                DateTimeOffset? time = Assert.Single(service.ManagementSnapshot.Devices).LastConnectedAt;
                await service.SaveDevicePreferencesAsync(device.DeviceKey, "手机A03", true, CancellationToken.None);
                Assert.Equal("手机A03", Assert.Single(service.Snapshot.Devices).DisplayName);
                Assert.Equal(time, Assert.Single(service.ManagementSnapshot.Devices).LastConnectedAt);
                Assert.True(Assert.Single(service.ManagementSnapshot.Devices).AutoConnect);
                Assert.True(service.TryResolveReadyDevice(null, out var renamed));
                Assert.Equal(original.SessionEpoch, renamed.SessionEpoch);
                Assert.Equal(original.Serial, renamed.Serial);
                AndroidConnectionException invalid = await Assert.ThrowsAsync<AndroidConnectionException>(() =>
                    service.RenameDeviceAsync(device.DeviceKey, "bad name😀", CancellationToken.None).AsTask());
                Assert.Equal("CONNECTION_NAME_INVALID", invalid.ReasonCode);
                Assert.Equal("手机A03", Assert.Single(service.ManagementSnapshot.Devices).CustomName);
                await Assert.ThrowsAsync<AndroidConnectionException>(() => service.SaveDevicePreferencesAsync("missing", "手机4", false, CancellationToken.None).AsTask());
            }
            await using AndroidConnectionService restarted = CreateManagedService(client, path);
            await restarted.ScanOnceForTestAsync(CancellationToken.None);
            Assert.Equal("手机A03", Assert.Single(restarted.Snapshot.Devices).DisplayName);
            client.Devices = [];
            await restarted.ScanOnceForTestAsync(CancellationToken.None);
            ManagedAndroidDevice history = Assert.Single(restarted.ManagementSnapshot.Devices);
            Assert.Equal("手机A03", history.DisplayName);
            await restarted.RenameDeviceAsync(history.DeviceKey, "历史手机5", CancellationToken.None);
            Assert.Equal("历史手机5", Assert.Single(restarted.ManagementSnapshot.Devices).DisplayName);
            await restarted.ForgetDeviceAsync(history.DeviceKey, CancellationToken.None);
            Assert.Empty(restarted.ManagementSnapshot.Devices);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ExplicitWirelessConnectRecordsEndpointForServiceNamedPhoneAndRecoveryInvalidatesOldSession()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"connection-endpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "connections.json");
            const string name = "adb-example._adb-tls-connect._tcp";
            var device = Assert.Single(AdbOutputParser.ParseDevices($"{name} device model:Phone"));
            FakeAdbClient client = new([]) { WirelessService = name };
            ManagedRecoveryRunner runner = new("adb-example _adb-tls-connect._tcp 192.168.1.20:37002", () => client.Devices = [device]);
            await using AndroidConnectionService service = new(Path.GetTempPath(), NullAndroidConnectionLog.Instance,
                new AndroidConnectionServiceOptions { ConnectionPreferencesPath = path }, new FakeAdbServerShutdown(),
                _ => ValueTask.FromResult<IAdbCommandRunner>(runner));
            service.SetClientForTest(client);
            await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessManual, CancellationToken.None);
            AndroidOperationResult connected = await service.ExecuteWirelessAsync(WirelessAdbOperation.Connect,
                "192.168.1.20:37001", "", CancellationToken.None);
            Assert.True(connected.Succeeded);
            ConnectionPreferencesStore reloaded = new(path);
            await reloaded.InitializeAsync(CancellationToken.None);
            SavedAndroidConnection remembered = Assert.Single(reloaded.Value.Devices);
            Assert.Equal("192.168.1.20:37001", remembered.Endpoint);
            Assert.Equal(name, remembered.ServiceName);
            Assert.NotNull(remembered.LastConnectedAt);
            await service.SelectDeviceAsync(device.DeviceKey, CancellationToken.None);
            await service.SetDeviceAutoConnectAsync(device.DeviceKey, true, CancellationToken.None);
            Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice initial));
            client.Devices = [];
            await service.ScanOnceForTestAsync(CancellationToken.None);
            Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice recovered));
            Assert.True(recovered.SessionEpoch > initial.SessionEpoch);
            Assert.Equal("192.168.1.20:37002", runner.ConnectEndpoints[^1]);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ManagedWirelessRecoveryRetriesNewestEnabledHistoryOnlyAndNeverStealsUsbOrRepeatsWhileReady()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"connection-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "connections.json");
            const string mdns = "adb-recent _adb-tls-connect._tcp 192.168.1.20:37001\nadb-old _adb-tls-connect._tcp 192.168.1.21:37002";
            var usb = Assert.Single(AdbOutputParser.ParseDevices("usb device usb:1 model:Usb"));
            var recent = Assert.Single(AdbOutputParser.ParseDevices("adb-recent._adb-tls-connect._tcp device model:Recent"));
            ConnectionPreferencesStore store = new(path);
            await store.SaveAsync(new(1, PhoneConnectionMethod.WirelessScan,
            [
                new("old", "Old", "Old", AndroidTransport.Network, true, DateTimeOffset.UtcNow.AddDays(-2), "192.168.1.21:37002", "adb-old._adb-tls-connect._tcp"),
                new(recent.DeviceKey, "Recent", "Recent", AndroidTransport.Network, true, DateTimeOffset.UtcNow.AddDays(-1), "192.168.1.20:30000", recent.Serial),
                new("disabled", "Disabled", "Disabled", AndroidTransport.Network, false, DateTimeOffset.UtcNow, "192.168.1.22:37003"),
            ]), CancellationToken.None);
            FakeAdbClient client = new([usb]) { Services = mdns };
            ManagedRecoveryRunner runner = new(mdns, () => client.Devices = [usb, recent]);
            await using AndroidConnectionService service = new(Path.GetTempPath(), NullAndroidConnectionLog.Instance,
                new AndroidConnectionServiceOptions { ConnectionPreferencesPath = path }, new FakeAdbServerShutdown(),
                _ => ValueTask.FromResult<IAdbCommandRunner>(runner));
            service.SetClientForTest(client);
            await service.ScanOnceForTestAsync(CancellationToken.None);
            Assert.Equal(recent.DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
            Assert.Equal("192.168.1.20:37001", Assert.Single(runner.ConnectEndpoints));
            await service.ScanOnceForTestAsync(CancellationToken.None);
            Assert.Single(runner.ConnectEndpoints);
            Assert.Equal(2, service.ManagementSnapshot.Devices.Count(device => !device.IsAvailable));
            await service.DisconnectSelectionAsync(CancellationToken.None);
            client.Devices = [usb];
            await service.ScanOnceForTestAsync(CancellationToken.None);
            Assert.Single(runner.ConnectEndpoints);
            Assert.False(service.Snapshot.IsReady);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ManagedDefaultRequiresUsbSelectionAndRemembersSuccessfulDevicesAcrossRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"connection-management-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "connections.json");
            var devices = AdbOutputParser.ParseDevices("one device usb:1 model:First\n192.168.1.20:37001 device model:Wireless");
            FakeAdbClient client = new(devices);
            await using (AndroidConnectionService service = CreateManagedService(client, path))
            {
                await service.ScanOnceForTestAsync(CancellationToken.None);
                Assert.Equal(PhoneConnectionMethod.Usb, service.ManagementSnapshot.Method);
                Assert.False(service.Snapshot.IsReady);
                Assert.Single(service.ManagementSnapshot.Devices);
                Assert.Equal(2, service.Snapshot.Devices.Count);
                await service.SelectDeviceAsync(devices[0].DeviceKey, CancellationToken.None);
                Assert.True(service.Snapshot.IsReady);
                Assert.NotNull(Assert.Single(service.ManagementSnapshot.Devices, value => value.DeviceKey == devices[0].DeviceKey).LastConnectedAt);
                await service.SetDeviceAutoConnectAsync(devices[0].DeviceKey, true, CancellationToken.None);
            }
            client.Devices = [devices[0]];
            await using AndroidConnectionService restarted = CreateManagedService(client, path);
            await restarted.ScanOnceForTestAsync(CancellationToken.None);
            Assert.Equal(devices[0].DeviceKey, restarted.Snapshot.SelectedDevice?.DeviceKey);
            Assert.True(Assert.Single(restarted.ManagementSnapshot.Devices).AutoConnect);
            client.Devices = [];
            await restarted.ScanOnceForTestAsync(CancellationToken.None);
            ManagedAndroidDevice history = Assert.Single(restarted.ManagementSnapshot.Devices);
            Assert.False(history.IsAvailable);
            await restarted.ForgetDeviceAsync(history.DeviceKey, CancellationToken.None);
            Assert.Empty(restarted.ManagementSnapshot.Devices);
            ConnectionPreferencesStore reloaded = new(path);
            await reloaded.InitializeAsync(CancellationToken.None);
            Assert.Empty(reloaded.Value.Devices);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData(PhoneConnectionMethod.Usb, AndroidTransport.Usb)]
    [InlineData(PhoneConnectionMethod.WirelessScan, AndroidTransport.Network)]
    [InlineData(PhoneConnectionMethod.WirelessCode, AndroidTransport.Network)]
    [InlineData(PhoneConnectionMethod.WirelessManual, AndroidTransport.Network)]
    public async Task MultipleAutomaticDevicesChooseMostRecentlyConnectedAvailableCompatibleDevice(PhoneConnectionMethod method, AndroidTransport transport)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"connection-recency-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "connections.json");
            var devices = AdbOutputParser.ParseDevices("one device usb:1 model:First\ntwo device usb:2 model:Second\nthree device usb:3 model:Third")
                .Select(device => device with { Transport = transport }).ToArray();
            ConnectionPreferencesStore store = new(path);
            await store.SaveAsync(new(1, method,
            [
                new(devices[0].DeviceKey, "First", "First", transport, true, DateTimeOffset.UtcNow.AddDays(-2)),
                new(devices[1].DeviceKey, "Second", "Second", transport, true, DateTimeOffset.UtcNow.AddDays(-1)),
                new(devices[2].DeviceKey, "Third", "Third", transport, false, DateTimeOffset.UtcNow),
                new("absent", "Absent", "Absent", transport, true, DateTimeOffset.UtcNow),
            ]), CancellationToken.None);
            FakeAdbClient client = new(devices);
            await using AndroidConnectionService service = CreateManagedService(client, path);
            await service.ScanOnceForTestAsync(CancellationToken.None);
            Assert.Equal(devices[1].DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
            await service.SelectDeviceAsync(devices[0].DeviceKey, CancellationToken.None);
            await service.ScanOnceForTestAsync(CancellationToken.None);
            Assert.Equal(devices[0].DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
            await service.DisconnectSelectionAsync(CancellationToken.None);
            Assert.False(service.Snapshot.IsReady);
            await service.ScanOnceForTestAsync(CancellationToken.None);
            Assert.False(service.Snapshot.IsReady); // Explicit disconnect suppresses automatic selection for this run.
            await service.SelectDeviceAsync(devices[0].DeviceKey, CancellationToken.None);
            await service.ForgetDeviceAsync(devices[0].DeviceKey, CancellationToken.None);
            Assert.Equal(devices[1].DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
            Assert.False(Assert.Single(service.ManagementSnapshot.Devices, device => device.DeviceKey == devices[0].DeviceKey).AutoConnect);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ManagedMethodFiltersSelectionAndCorruptPreferencesSafelyUseUsb()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"connection-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "connections.json");
            await File.WriteAllTextAsync(path, "{invalid");
            var devices = AdbOutputParser.ParseDevices("one device usb:1 model:Usb\n192.168.1.20:37001 device model:Wireless");
            await using AndroidConnectionService service = CreateManagedService(new FakeAdbClient(devices), path);
            await service.ScanOnceForTestAsync(CancellationToken.None);
            Assert.Equal(PhoneConnectionMethod.Usb, service.ManagementSnapshot.Method);
            await Assert.ThrowsAsync<AndroidConnectionException>(() => service.SelectDeviceAsync(devices[1].DeviceKey, CancellationToken.None).AsTask());
            await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessCode, CancellationToken.None);
            Assert.False(service.Snapshot.IsReady);
            await service.SelectDeviceAsync(devices[1].DeviceKey, CancellationToken.None);
            Assert.Equal(AndroidTransport.Network, service.Snapshot.SelectedDevice?.Transport);
            await service.SetConnectionMethodAsync(PhoneConnectionMethod.Usb, CancellationToken.None);
            Assert.False(service.Snapshot.IsReady); // Saved opt-out does not reconnect on a method switch.
            await service.SelectDeviceAsync(devices[0].DeviceKey, CancellationToken.None);
            Assert.Equal(AndroidTransport.Usb, service.Snapshot.SelectedDevice?.Transport);
            ConnectionPreferencesStore reloaded = new(path);
            await reloaded.InitializeAsync(CancellationToken.None);
            Assert.Equal(PhoneConnectionMethod.Usb, reloaded.Value.Method);
            Assert.Single(service.ManagementSnapshot.Devices);
            Assert.Equal(2, reloaded.Value.Devices.Count(device => device.LastConnectedAt is not null));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static AndroidConnectionService CreateManagedService(FakeAdbClient client, string path)
    {
        AndroidConnectionService service = new(Path.GetTempPath(), NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions { ConnectionPreferencesPath = path }, new FakeAdbServerShutdown());
        service.SetClientForTest(client);
        return service;
    }

    [Fact]
    public async Task DeviceRowsEnrichAuthorizedPhonesOnlyAndCacheMetadata()
    {
        var devices = AdbOutputParser.ParseDevices("one device usb:1 model:First\ntwo device usb:2 model:Second\nthree unauthorized usb:3 model:Third");
        FakeAdbClient client = new(devices);
        await using AndroidConnectionService service = CreateService(client);
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.Equal(2, client.ProbeCount);
        Assert.All(service.Snapshot.Devices.Where(device => device.Status == AndroidDeviceStatus.Ready),
            device => Assert.Equal("15", device.AndroidVersion));
        Assert.Equal("", Assert.Single(service.Snapshot.Devices, device => device.Status == AndroidDeviceStatus.Unauthorized).AndroidVersion);
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.Equal(2, client.ProbeCount);
    }

    [Fact]
    public async Task SecondaryMetadataFailureKeepsReadySessionAndWaitsBeforeRetry()
    {
        var devices = AdbOutputParser.ParseDevices("one device usb:1 model:First\ntwo device usb:2 model:Second");
        FakeAdbClient client = new(devices) { FailingSerial = "two" };
        await using AndroidConnectionService service = CreateService(client);
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.Snapshot.IsReady);
        Assert.True(service.TryResolveReadyDevice(null, out var first));
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out var second));
        Assert.Equal(first.SessionEpoch, second.SessionEpoch);
        Assert.Equal(2, client.ProbeCount);
    }

    [Fact]
    public void DefaultCommandTimeoutAllowsColdAdbDaemonStartup()
    {
        AndroidConnectionServiceOptions options = new();

        Assert.Equal(TimeSpan.FromSeconds(15), options.CommandTimeout);
    }

    [Fact]
    public async Task DisposeStopsTheBundledAdbServerExactlyOnce()
    {
        FakeAdbClient client = new([]);
        FakeAdbServerShutdown adbServerShutdown = new();
        AndroidConnectionService service = CreateService(client, adbServerShutdown);

        await service.DisposeAsync();
        await service.DisposeAsync();

        Assert.Equal(1, adbServerShutdown.StopCount);
        Assert.Equal(AndroidConnectionState.Stopped, service.Snapshot.State);
    }

    [Fact]
    public async Task AuthorizedUsbDeviceBecomesReadyWithProbedDetails()
    {
        AdbDeviceRecord device = Assert.Single(AdbOutputParser.ParseDevices("""
            List of devices attached
            test-serial device usb:1-1 model:Test_Phone device:test

            """));
        FakeAdbClient client = new([device]);
        await using AndroidConnectionService service = CreateService(client);

        await service.ScanOnceForTestAsync(CancellationToken.None);

        AndroidConnectionSnapshot snapshot = service.Snapshot;
        Assert.Equal(AndroidConnectionState.Ready, snapshot.State);
        Assert.Equal("ANDROID_READY", snapshot.ReasonCode);
        Assert.Equal("Test Phone", snapshot.SelectedDevice?.Model);
        Assert.Equal("15", snapshot.SelectedDevice?.AndroidVersion);
        Assert.Equal(1080, snapshot.SelectedDevice?.NativeDisplayWidth);
        Assert.Equal(2400, snapshot.SelectedDevice?.NativeDisplayHeight);
        Assert.True(snapshot.SelectedDevice?.Capabilities.Video.IsAvailable);
        Assert.True(snapshot.SelectedDevice?.Capabilities.InternalAudio.IsAvailable);
        Assert.DoesNotContain("test-serial", snapshot.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("192.168.1.20:37001")]
    [InlineData("adb-private-token._adb-tls-connect._tcp")]
    public async Task WirelessDeviceUsesExistingProbeAndSessionLifecycle(string serial)
    {
        AdbDeviceRecord device = Assert.Single(AdbOutputParser.ParseDevices(
            $"{serial} device model:Wireless_Phone"));
        FakeAdbClient client = new([device]);
        await using AndroidConnectionService service = CreateService(client);
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.Snapshot.IsReady);
        Assert.Equal(AndroidTransport.Network, service.Snapshot.SelectedDevice?.Transport);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice first));
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice second));
        Assert.Equal(serial, second.Serial);
        Assert.Equal(first.SessionEpoch, second.SessionEpoch);
        Assert.Equal(1, client.ProbeCount);
        Assert.DoesNotContain(serial, service.Snapshot.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnauthorizedDeviceRequestsInlineAuthorization()
    {
        AdbDeviceRecord device = Assert.Single(AdbOutputParser.ParseDevices("""
            List of devices attached
            private-serial unauthorized usb:1-1 model:Locked_Phone

            """));
        FakeAdbClient client = new([device]);
        await using AndroidConnectionService service = CreateService(client);

        await service.ScanOnceForTestAsync(CancellationToken.None);

        AndroidConnectionSnapshot snapshot = service.Snapshot;
        Assert.Equal(AndroidConnectionState.AuthorizationRequired, snapshot.State);
        Assert.Equal("ANDROID_AUTHORIZATION_REQUIRED", snapshot.ReasonCode);
        Assert.Contains("允许 USB 调试", snapshot.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-serial", snapshot.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionEpochRemainsStableWhileUsbTransportStaysReady()
    {
        AdbDeviceRecord device = Assert.Single(AdbOutputParser.ParseDevices("""
            List of devices attached
            test-serial device usb:1-1 model:Test_Phone device:test

            """));
        FakeAdbClient client = new([device]);
        await using AndroidConnectionService service = CreateService(client);

        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice first));
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice second));

        Assert.Equal(first.SessionEpoch, second.SessionEpoch);
        Assert.True(first.SessionEpoch > 0);
        Assert.Equal(1, client.ProbeCount);
    }

    [Fact]
    public async Task SessionEpochAdvancesAfterUsbDisconnectAndReconnect()
    {
        AdbDeviceRecord device = Assert.Single(AdbOutputParser.ParseDevices("""
            List of devices attached
            test-serial device usb:1-1 model:Test_Phone device:test

            """));
        FakeAdbClient client = new([device]);
        await using AndroidConnectionService service = CreateService(client);

        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice first));

        client.Devices = [];
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.False(service.TryResolveReadyDevice(null, out _));

        client.Devices = [device];
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice second));

        Assert.True(second.SessionEpoch > first.SessionEpoch);
        Assert.Equal(2, client.ProbeCount);
    }

    [Fact]
    public async Task ConnectingWirelessDoesNotStealReadyUsbAndExplicitSelectionChangesEpoch()
    {
        AdbDeviceRecord usb = Assert.Single(AdbOutputParser.ParseDevices("usb-example device usb:1 model:Phone"));
        AdbDeviceRecord network = Assert.Single(AdbOutputParser.ParseDevices("192.168.1.20:37001 device model:Phone"));
        FakeAdbClient client = new([usb]);
        ConnectionRunner runner = new(() => client.Devices = [usb, network]);
        await using AndroidConnectionService service = new(Path.GetTempPath(), NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions(), new FakeAdbServerShutdown(),
            _ => ValueTask.FromResult<IAdbCommandRunner>(runner));
        service.SetClientForTest(client);
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice initial));
        AndroidOperationResult result = await service.ExecuteWirelessAsync(WirelessAdbOperation.Connect,
            "192.168.1.20:37001", "", CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(usb.DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice unchanged));
        Assert.Equal(initial.SessionEpoch, unchanged.SessionEpoch);
        await service.SelectDeviceAsync(network.DeviceKey, CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice selected));
        Assert.Equal(network.Serial, selected.Serial);
        Assert.True(selected.SessionEpoch > initial.SessionEpoch);
        client.Devices = [network with { Status = AndroidDeviceStatus.Offline }];
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.False(service.TryResolveReadyDevice(null, out _));
        Assert.Contains("无线", service.Snapshot.Message, StringComparison.Ordinal);
        client.Devices = [network];
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out ResolvedAndroidDevice reconnected));
        Assert.True(reconnected.SessionEpoch > selected.SessionEpoch);
    }

    [Fact]
    public async Task ConnectDoesNotReportSuccessForAnUnrelatedReadyWirelessPhone()
    {
        AdbDeviceRecord other = Assert.Single(AdbOutputParser.ParseDevices("192.168.1.21:37001 device model:Other"));
        FakeAdbClient client = new([other]);
        ConnectionRunner runner = new(() => { });
        await using AndroidConnectionService service = new(Path.GetTempPath(), NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions(), new FakeAdbServerShutdown(),
            _ => ValueTask.FromResult<IAdbCommandRunner>(runner));
        service.SetClientForTest(client);
        await service.ScanOnceForTestAsync(CancellationToken.None);
        AndroidOperationResult result = await service.ExecuteWirelessAsync(WirelessAdbOperation.Connect,
            "192.168.1.20:37001", "", CancellationToken.None);
        Assert.Equal("WIRELESS_NOT_READY", result.ReasonCode);
        Assert.Equal(other.DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
    }

    [Fact]
    public async Task ConnectRecognizesTheRequestedPhoneAlreadyListedByMdnsServiceName()
    {
        const string serial = "adb-example._adb-tls-connect._tcp";
        AdbDeviceRecord network = Assert.Single(AdbOutputParser.ParseDevices($"{serial} device model:Wireless"));
        FakeAdbClient client = new([network]) { WirelessService = serial };
        ConnectionRunner runner = new(() => { });
        await using AndroidConnectionService service = new(Path.GetTempPath(), NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions(), new FakeAdbServerShutdown(),
            _ => ValueTask.FromResult<IAdbCommandRunner>(runner));
        service.SetClientForTest(client);
        await service.ScanOnceForTestAsync(CancellationToken.None);
        AndroidOperationResult result = await service.ExecuteWirelessAsync(WirelessAdbOperation.Connect,
            "192.168.1.20:37001", "", CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal("WIRELESS_READY", result.ReasonCode);
        Assert.Equal(network.DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
    }

    private sealed class ConnectionRunner(Action connected) : IAdbCommandRunner
    {
        public ValueTask<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, string? standardInput = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            connected();
            return ValueTask.FromResult(new AdbCommandResult(0,
                "connected to " + arguments[1], "", TimeSpan.Zero, false));
        }
    }

    private static AndroidConnectionService CreateService(
        IAdbClient client,
        IAdbServerShutdown? adbServerShutdown = null)
    {
        AndroidConnectionService service = new(
            Path.GetTempPath(),
            NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions(),
            adbServerShutdown ?? new FakeAdbServerShutdown());
        service.SetClientForTest(client);
        return service;
    }

    private sealed class FakeAdbServerShutdown : IAdbServerShutdown
    {
        private int _stopCount;

        public int StopCount => Volatile.Read(ref _stopCount);

        public ValueTask<AdbServerShutdownResult> StopAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _stopCount);
            return ValueTask.FromResult(new AdbServerShutdownResult(
                true,
                AndroidReasonCodes.AdbServerStopped,
                "stopped"));
        }
    }

    private sealed class FakeAdbClient : IAdbClient
    {
        private int _probeCount;

        public FakeAdbClient(IReadOnlyList<AdbDeviceRecord> devices)
        {
            Devices = devices;
        }

        public IReadOnlyList<AdbDeviceRecord> Devices { get; set; }

        public int ProbeCount => Volatile.Read(ref _probeCount);

        public string? WirelessService { get; init; }
        public string? Services { get; init; }
        public string? FailingSerial { get; init; }

        public ValueTask<string?> ResolveWirelessServiceAsync(string endpoint, CancellationToken cancellationToken) =>
            ValueTask.FromResult(WirelessService);

        public ValueTask<AdbListResult> ListDevicesAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AdbListResult(
                Devices,
                TimeSpan.FromMilliseconds(2),
                "test", Services));

        public ValueTask<AdbProbeResult> ProbeAsync(
            string serial,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _probeCount);
            if (serial == FailingSerial) { throw new AndroidConnectionException("TEST_METADATA_FAILED", "metadata unavailable"); }
            return ValueTask.FromResult(new AdbProbeResult(
                true,
                "ANDROID_PROBE_READY",
                "ready",
                "Test",
                "Test",
                "Test Phone",
                "test",
                "15",
                35,
                "arm64-v8a",
                1080,
                2400,
                TimeSpan.FromMilliseconds(3)));
        }
    }

    private sealed class ManagedRecoveryRunner(string services, Action connected) : IAdbCommandRunner
    {
        public List<string> ConnectEndpoints { get; } = [];
        public ValueTask<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, string? standardInput = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (arguments[0] == "connect")
            {
                ConnectEndpoints.Add(arguments[1]);
                connected();
                return ValueTask.FromResult(new AdbCommandResult(0, "connected to " + arguments[1], "", TimeSpan.Zero, false));
            }
            Assert.Equal("mdns", arguments[0]);
            return ValueTask.FromResult(new AdbCommandResult(0, services, "", TimeSpan.Zero, false));
        }
    }
}
