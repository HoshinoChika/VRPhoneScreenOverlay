using System.Text.Json;
using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhysicalDeviceManagementTests : IDisposable
{
    [Fact]
    public async Task SecondaryWirelessIdentityDiscoveryPreservesItsExistingAutomaticEligibility()
    {
        var routes = AdbOutputParser.ParseDevices("192.168.1.30:37001 device model:SameModel\n192.168.1.31:37002 device model:SameModel");
        ConnectionPreferencesStore store = new(Preferences);
        await store.SaveAsync(new(1, PhoneConnectionMethod.WirelessScan, routes.Select((route, index) =>
            new SavedAndroidConnection(route.DeviceKey, "Phone", "SameModel", AndroidTransport.Network, true,
                DateTimeOffset.UtcNow.AddDays(-index))).ToArray()), CancellationToken.None);
        Client client = new(routes)
        { PhysicalIdentities = new Dictionary<string, string> { [routes[0].Serial] = "first", [routes[1].Serial] = "second" } };
        await using AndroidConnectionService service = Create(client);
        await Scan(service);
        Assert.Equal(routes[0].DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
        client.Devices = [routes[1]];
        await Scan(service);
        Assert.Equal(routes[1].DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
    }

    [Fact]
    public async Task NewAutomaticPreferenceIsAppliedOnMethodSelectionWithoutInventingHistory()
    {
        Client client = new(Routes());
        await using AndroidConnectionService service = Create(client);
        await Scan(service);
        string usb = Assert.Single(service.ManagementSnapshot.Devices).DeviceKey;
        await service.SaveDevicePreferencesAsync(usb, "USB名称", true, CancellationToken.None);
        await Scan(service);
        Assert.False(service.Snapshot.IsReady);
        Assert.Null(Assert.Single(service.ManagementSnapshot.Devices).LastConnectedAt);
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.Usb, false, CancellationToken.None);
        Assert.True(service.Snapshot.IsReady);
        Assert.NotNull(Assert.Single(service.ManagementSnapshot.Devices).LastConnectedAt);
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessScan, false, CancellationToken.None);
        Assert.False(service.Snapshot.IsReady); // USB preference cannot authorize wireless.
        string wireless = Assert.Single(service.ManagementSnapshot.Devices).DeviceKey;
        await service.SelectDeviceAsync(wireless, CancellationToken.None);
        client.Devices = Routes().Where(route => route.Transport == AndroidTransport.Usb).ToArray();
        await Scan(service);
        ManagedAndroidDevice history = Assert.Single(service.ManagementSnapshot.Devices);
        Assert.Equal(AndroidTransport.Network, history.Transport);
        Assert.False(history.IsAvailable);
    }

    [Fact]
    public async Task BrowsingWirelessKeepsUsbLeaseUntilItDropsThenChoosesNewestWirelessRoute()
    {
        var routes = Routes();
        string physical = AdbOutputParser.CreateDeviceKey("tablet");
        ConnectionPreferencesStore store = new(Preferences);
        await store.SaveAsync(new(1, PhoneConnectionMethod.Usb, routes.Select((route, index) =>
            new SavedAndroidConnection(route.DeviceKey, "Tablet", "SameModel", route.Transport, true,
                DateTimeOffset.UtcNow.AddDays(index - 3), PhysicalDeviceKey: physical)).ToArray()), CancellationToken.None);
        Client client = new(routes);
        await using AndroidConnectionService service = Create(client);
        await Scan(service);
        Assert.True(service.TryResolveReadyDevice(null, out var before));
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessScan, true, CancellationToken.None);
        await Scan(service);
        Assert.True(service.TryResolveReadyDevice(null, out var after));
        Assert.Equal(before.SessionEpoch, after.SessionEpoch);
        Assert.Equal(before.DeviceKey, after.DeviceKey);
        Assert.False(Assert.Single(service.ManagementSnapshot.Devices).IsSelected);
        client.Devices = routes.Where(route => route.Transport == AndroidTransport.Network).ToArray();
        await Scan(service);
        Assert.Equal(routes[2].DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
        Assert.True(Assert.Single(service.ManagementSnapshot.Devices).IsSelected);
        client.Devices = [routes[0]];
        await Scan(service);
        Assert.False(service.Snapshot.IsReady); // No cross-transport fallback.
        ManagedAndroidDevice history = Assert.Single(service.ManagementSnapshot.Devices);
        Assert.Equal(AndroidTransport.Network, history.Transport);
        Assert.False(history.IsAvailable);
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.Usb, false, CancellationToken.None);
        Assert.Equal(routes[0].DeviceKey, service.Snapshot.SelectedDevice?.DeviceKey);
    }

    [Fact]
    public async Task WirelessPreferencesAndForgetCannotChangeOrCloseTheUsbSession()
    {
        var routes = Routes();
        string physical = AdbOutputParser.CreateDeviceKey("tablet");
        ConnectionPreferencesStore store = new(Preferences);
        await store.SaveAsync(new(1, PhoneConnectionMethod.Usb, routes.Select(route =>
            new SavedAndroidConnection(route.DeviceKey, "Tablet", "SameModel", route.Transport, true,
                DateTimeOffset.UtcNow, CustomName: "旧名称", PhysicalDeviceKey: physical)).ToArray()), CancellationToken.None);
        Client client = new(routes);
        await using AndroidConnectionService service = Create(client);
        await Scan(service);
        Assert.True(service.TryResolveReadyDevice(null, out var before));
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessScan, true, CancellationToken.None);
        string wireless = Assert.Single(service.ManagementSnapshot.Devices).DeviceKey;
        await service.SaveDevicePreferencesAsync(wireless, "无线名称", false, CancellationToken.None);
        Assert.False(Assert.Single(service.ManagementSnapshot.Devices).AutoConnect);
        await service.ForgetDeviceAsync(wireless, CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out var after));
        Assert.Equal(before.SessionEpoch, after.SessionEpoch);
        ConnectionPreferencesStore reloaded = new(Preferences);
        await reloaded.InitializeAsync(CancellationToken.None);
        SavedAndroidConnection usb = Assert.Single(reloaded.Value.Devices);
        Assert.Equal(AndroidTransport.Usb, usb.Transport);
        Assert.True(usb.AutoConnect);
        Assert.Equal("旧名称", usb.CustomName);
        Assert.NotNull(usb.LastConnectedAt);
    }

    [Fact]
    public async Task UsbHistoryRemainsVisibleWhenOnlyTheWirelessPathIsOnline()
    {
        var routes = Routes();
        Client client = new(routes);
        await using AndroidConnectionService service = Create(client);
        await Scan(service);
        await service.SelectDeviceAsync(routes[0].DeviceKey, CancellationToken.None);
        client.Devices = routes.Where(route => route.Transport == AndroidTransport.Network).ToArray();
        await Scan(service);
        ManagedAndroidDevice history = Assert.Single(service.ManagementSnapshot.Devices);
        Assert.False(history.IsAvailable);
        Assert.Equal(AndroidTransport.Usb, history.Transport);
        Assert.NotNull(history.LastConnectedAt);
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "physical-devices-" + Guid.NewGuid().ToString("N"));
    private string Preferences => Path.Combine(_directory, "connections.json");
    public PhysicalDeviceManagementTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task LegacyUsbAndMultipleWirelessPathsAreOneDeviceAcrossListsAndRestart()
    {
        var routes = Routes();
        ConnectionPreferences legacy = new(1, PhoneConnectionMethod.Usb,
            routes.Select(route => new SavedAndroidConnection(route.DeviceKey, "ModelCode", "SameModel", route.Transport, true,
                DateTimeOffset.UtcNow, route.Transport == AndroidTransport.Network ? route.Serial : null,
                route.Transport == AndroidTransport.Network ? "adb-tablet-old._adb-tls-connect._tcp" : null, "我的平板")).ToArray());
        await File.WriteAllTextAsync(Preferences, JsonSerializer.Serialize(legacy));
        Client client = new(routes);
        await using (AndroidConnectionService service = Create(client))
        {
            await Scan(service);
            ManagedAndroidDevice current = Assert.Single(service.ManagementSnapshot.Devices);
            Assert.True(current.IsSelected);
            Assert.Equal("我的平板", current.DisplayName);
            await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessScan, CancellationToken.None);
            current = Assert.Single(service.ManagementSnapshot.Devices);
            Assert.True(current.IsSelected);
            await service.RenameDeviceAsync(current.DeviceKey, "新平板名称", CancellationToken.None);
            Assert.False(service.IsSelectedDevice(routes[0].DeviceKey)); // USB and wireless management are independent.
            await service.SetDeviceAutoConnectAsync(current.DeviceKey, false, CancellationToken.None);
        }
        await using AndroidConnectionService restarted = Create(client);
        await Scan(restarted);
        ManagedAndroidDevice available = Assert.Single(restarted.ManagementSnapshot.Devices);
        Assert.Equal("新平板名称", available.DisplayName);
        Assert.False(available.AutoConnect);
        Assert.False(available.IsSelected);
        client.Devices = [];
        await Scan(restarted);
        ManagedAndroidDevice historical = Assert.Single(restarted.ManagementSnapshot.Devices);
        Assert.False(historical.IsAvailable);
        Assert.Equal("新平板名称", historical.DisplayName);
        await restarted.ForgetDeviceAsync(historical.DeviceKey, CancellationToken.None);
        Assert.Empty(restarted.ManagementSnapshot.Devices);
        client.Devices = routes;
        await Scan(restarted);
        ManagedAndroidDevice fresh = Assert.Single(restarted.ManagementSnapshot.Devices);
        Assert.Equal("固件平板名称", fresh.DisplayName);
        Assert.False(fresh.AutoConnect);
        Assert.Null(fresh.CustomName);
        Assert.Null(fresh.LastConnectedAt);
    }

    [Fact]
    public async Task AtomicSaveDoesNotTouchCurrentLeaseAndDisabledDeviceIsNotReconnectedAfterDropOrSwitch()
    {
        Client client = new(Routes());
        await using AndroidConnectionService service = Create(client);
        await Scan(service);
        string usb = Assert.Single(service.ManagementSnapshot.Devices).DeviceKey;
        await service.SelectDeviceAsync(usb, CancellationToken.None);
        Assert.True(service.TryResolveReadyDevice(null, out var before));
        int scans = client.ScanCount;
        int probes = client.ProbeCount;
        await service.SaveDevicePreferencesAsync(usb, "保存后的平板", false, CancellationToken.None);
        Assert.Equal(scans, client.ScanCount);
        Assert.Equal(probes, client.ProbeCount);
        Assert.True(service.TryResolveReadyDevice(null, out var after));
        Assert.Equal(before.SessionEpoch, after.SessionEpoch);
        Assert.Equal(before.DeviceKey, after.DeviceKey);
        Assert.Equal("保存后的平板", Assert.Single(service.ManagementSnapshot.Devices).DisplayName);
        Directory.CreateDirectory(Preferences + ".tmp");
        await Assert.ThrowsAsync<AndroidConnectionException>(() => service.SaveDevicePreferencesAsync(usb, "失败修改", true, CancellationToken.None).AsTask());
        Directory.Delete(Preferences + ".tmp");
        ManagedAndroidDevice unchanged = Assert.Single(service.ManagementSnapshot.Devices);
        Assert.Equal("保存后的平板", unchanged.DisplayName);
        Assert.False(unchanged.AutoConnect);
        Assert.True(service.TryResolveReadyDevice(null, out var retained));
        Assert.Equal(before.SessionEpoch, retained.SessionEpoch);
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessCode, CancellationToken.None);
        Assert.False(service.Snapshot.IsReady);
        await Scan(service);
        Assert.False(service.Snapshot.IsReady);
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.Usb, CancellationToken.None);
        Assert.False(service.Snapshot.IsReady);
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessCode, CancellationToken.None);
        Assert.False(service.Snapshot.IsReady);
        string wireless = Assert.Single(service.ManagementSnapshot.Devices).DeviceKey;
        await service.SelectDeviceAsync(wireless, CancellationToken.None);
        Assert.True(service.Snapshot.IsReady); // An explicit Connect remains allowed.
        client.Devices = [];
        await Scan(service);
        Assert.False(service.Snapshot.IsReady);
        client.Devices = Routes();
        await Scan(service);
        Assert.False(service.Snapshot.IsReady);
        await Assert.ThrowsAsync<AndroidConnectionException>(() => service.SaveDevicePreferencesAsync(wireless, "不合法 名", true, CancellationToken.None).AsTask());
        Assert.False(Assert.Single(service.ManagementSnapshot.Devices).AutoConnect);
        Assert.Equal("固件平板名称", Assert.Single(service.ManagementSnapshot.Devices).DisplayName);
    }

    [Fact]
    public async Task AutomaticPreferenceIsConfigurationOnlyAndSurvivesNewWirelessPort()
    {
        Client client = new(Routes());
        await using (AndroidConnectionService service = Create(client))
        {
            await Scan(service);
            string usb = Assert.Single(service.ManagementSnapshot.Devices).DeviceKey;
            await service.SetDeviceAutoConnectAsync(usb, true, CancellationToken.None);
            await Scan(service);
            Assert.False(service.Snapshot.IsReady);
            await service.SelectDeviceAsync(usb, CancellationToken.None);
            await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessCode, CancellationToken.None);
            string wireless = Assert.Single(service.ManagementSnapshot.Devices).DeviceKey;
            await service.SelectDeviceAsync(wireless, CancellationToken.None);
            await service.SetDeviceAutoConnectAsync(wireless, true, CancellationToken.None);
        }
        client.Devices = AdbOutputParser.ParseDevices("192.168.1.30:39009 device model:SameModel");
        await using AndroidConnectionService restarted = Create(client);
        await Scan(restarted);
        Assert.True(restarted.Snapshot.IsReady);
        ManagedAndroidDevice current = Assert.Single(restarted.ManagementSnapshot.Devices);
        Assert.True(current.IsSelected);
        Assert.True(current.AutoConnect);
        Assert.Equal("固件平板名称", current.DisplayName);
        await restarted.SetDeviceAutoConnectAsync(current.DeviceKey, false, CancellationToken.None);
        Assert.True(restarted.Snapshot.IsReady);
    }

    [Fact]
    public async Task AliasCollapseStillMigratesIpOnlyHistoryAndFailedScanKeepsKnownMembership()
    {
        var routes = AdbOutputParser.ParseDevices("tablet device usb:1 model:SameModel\n192.168.1.30:37001 device model:SameModel\nadb-tablet-new._adb-tls-connect._tcp device model:SameModel");
        await File.WriteAllTextAsync(Preferences, JsonSerializer.Serialize(new ConnectionPreferences(1, PhoneConnectionMethod.WirelessScan,
            [new(routes[1].DeviceKey, "旧型号", "SameModel", AndroidTransport.Network, false, DateTimeOffset.UtcNow, routes[1].Serial)])));
        Client client = new(routes) { Services = "adb-tablet-new _adb-tls-connect._tcp 192.168.1.30:37001" };
        await using AndroidConnectionService service = Create(client);
        await Scan(service);
        Assert.Single(service.ManagementSnapshot.Devices);
        client.FailScan = true;
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(Assert.Single(service.ManagementSnapshot.Devices).IsAvailable);
    }

    [Fact]
    public async Task IdenticalModelsNeverMergeAndOtherTransportIsNotOfflineHistory()
    {
        var routes = AdbOutputParser.ParseDevices("tablet device usb:1 model:SameModel\nother device usb:2 model:SameModel");
        Client client = new(routes);
        await using AndroidConnectionService service = Create(client);
        await Scan(service);
        Assert.Equal(2, service.ManagementSnapshot.Devices.Count);
        await service.SelectDeviceAsync(routes[0].DeviceKey, CancellationToken.None);
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.WirelessManual, CancellationToken.None);
        Assert.Empty(service.ManagementSnapshot.Devices); // USB is present, but not in the wireless list or offline history.
        client.Devices = [];
        await Scan(service);
        Assert.Empty(service.ManagementSnapshot.Devices);
        await service.SetConnectionMethodAsync(PhoneConnectionMethod.Usb, CancellationToken.None);
        Assert.Single(service.ManagementSnapshot.Devices);
    }

    private static IReadOnlyList<AdbDeviceRecord> Routes() => AdbOutputParser.ParseDevices("tablet device usb:1 model:SameModel\n192.168.1.30:37001 device model:SameModel\n192.168.1.30:39001 device model:SameModel");
    private static async Task Scan(AndroidConnectionService service)
    { for (int index = 0; index < 4; index++) { await service.ScanOnceForTestAsync(CancellationToken.None); } }
    private AndroidConnectionService Create(Client client)
    {
        AndroidConnectionService service = new(_directory, NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions { ConnectionPreferencesPath = Preferences }, new Shutdown());
        service.SetClientForTest(client);
        return service;
    }
    public void Dispose() => Directory.Delete(_directory, recursive: true);
    private sealed class Shutdown : IAdbServerShutdown
    {
        public ValueTask<AdbServerShutdownResult> StopAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new AdbServerShutdownResult(true, "TEST", "test"));
    }
    private sealed class Client(IReadOnlyList<AdbDeviceRecord> devices) : IAdbClient
    {
        public IReadOnlyDictionary<string, string>? PhysicalIdentities { get; init; }
        public IReadOnlyList<AdbDeviceRecord> Devices { get; set; } = devices;
        public string? Services { get; init; }
        public bool FailScan { get; set; }
        public int ScanCount { get; private set; }
        public int ProbeCount { get; private set; }
        public ValueTask<AdbListResult> ListDevicesAsync(CancellationToken cancellationToken)
        {
            ScanCount++;
            if (FailScan) { throw new AndroidConnectionException("TEST_SCAN_FAILURE", "test"); }
            return ValueTask.FromResult(new AdbListResult(Devices, TimeSpan.Zero, "test", Services));
        }
        public ValueTask<string?> ResolveWirelessServiceAsync(string endpoint, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
        public ValueTask<AdbProbeResult> ProbeAsync(string serial, CancellationToken cancellationToken)
        {
            ProbeCount++;
            return ValueTask.FromResult(new AdbProbeResult(true, "TEST", "test", "Test", "Test", "SameModel", "test", "16", 36, "arm64", 1200, 2000, TimeSpan.Zero)
            {
                MarketName = "固件平板名称",
                PhysicalDeviceKey = AdbOutputParser.CreateDeviceKey(
                PhysicalIdentities?.GetValueOrDefault(serial) ?? (serial == "other" ? "other" : "tablet"))
            });
        }
    }
}
