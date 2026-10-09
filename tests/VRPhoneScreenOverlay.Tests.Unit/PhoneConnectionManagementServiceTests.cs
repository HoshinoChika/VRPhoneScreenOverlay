using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneConnectionManagementServiceTests
{
    [Fact]
    public async Task BrowsingAnotherTransportPreservesTheRunningOverlay()
    {
        await using Scenario scenario = new() { Method = PhoneConnectionMethod.Usb };
        await scenario.Service.ChangeMethodAsync(PhoneConnectionMethod.WirelessCode, CancellationToken.None);
        Assert.Equal(["method"], scenario.Operations);
        Assert.Equal(PhoneMediaSessionState.Running, scenario.MediaSnapshot.State);
        Assert.Equal("old", scenario.Snapshot.SelectedDevice?.DeviceKey);
    }

    [Fact]
    public async Task SwitchingTransportRearmsAutomaticOpeningWhenPreviousOverlayWasStopped()
    {
        await using Scenario scenario = new()
        {
            Method = PhoneConnectionMethod.Usb,
            MediaSnapshot = new(PhoneMediaSessionState.Stopped, "TEST", "test")
        };
        await scenario.Service.ChangeMethodAsync(PhoneConnectionMethod.WirelessScan, CancellationToken.None);
        Assert.Equal(["method", "media.rearm"], scenario.Operations);
    }

    [Fact]
    public async Task ChangingWirelessWorkflowPreservesTheReadyMediaSession()
    {
        await using Scenario scenario = new() { Method = PhoneConnectionMethod.WirelessScan };
        await scenario.Service.ChangeMethodAsync(PhoneConnectionMethod.WirelessCode, CancellationToken.None);
        Assert.Equal(["method"], scenario.Operations);
        Assert.Equal(PhoneMediaSessionState.Running, scenario.MediaSnapshot.State);
    }

    [Fact]
    public async Task NoAutomaticTargetLeavesTheStoppedSessionWaitingWithoutAnError()
    {
        await using Scenario scenario = new()
        { Method = PhoneConnectionMethod.Usb, NoTarget = true, MediaSnapshot = new(PhoneMediaSessionState.Stopped, "TEST", "test") };
        await scenario.Service.ChangeMethodAsync(PhoneConnectionMethod.WirelessScan, CancellationToken.None);
        Assert.Equal(["method", "media.rearm"], scenario.Operations);
        Assert.Equal(PhoneMediaSessionState.Stopped, scenario.MediaSnapshot.State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForgetStopsOnlyTheCurrentlySelectedDevice(bool selected)
    {
        await using Scenario scenario = new();
        await scenario.Service.ForgetAsync(selected ? "old" : "other", CancellationToken.None);
        Assert.Equal(selected ? "media.stop,forget" : "forget", string.Join(',', scenario.Operations));
    }

    [Fact]
    public async Task DisconnectSuppressesAutomaticMediaBeforeClearingSelection()
    {
        await using Scenario scenario = new();
        await scenario.Service.DisconnectAsync(CancellationToken.None);
        Assert.Equal(["media.stop", "disconnect"], scenario.Operations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalReconnectClosesOldMediaAndFailureNeverRestartsIt(bool fail)
    {
        await using Scenario scenario = new() { FailConnect = fail };
        if (fail)
        {
            await Assert.ThrowsAsync<AndroidConnectionException>(() => scenario.Service.ConnectAsync("new", CancellationToken.None));
            Assert.Equal(["media.pause", "connect"], scenario.Operations);
            Assert.Equal(PhoneMediaSessionState.Stopped, scenario.MediaSnapshot.State);
        }
        else
        {
            await scenario.Service.ConnectAsync("new", CancellationToken.None);
            Assert.Equal(["media.pause", "connect", "media.start.new"], scenario.Operations);
        }
    }

    [Fact]
    public async Task ExplicitReadyConnectionStartsStoppedOverlayWhenAutomaticOpenIsEnabled()
    {
        await using Scenario scenario = new()
        {
            Available = true,
            AutoOpen = true,
            MediaSnapshot = new(PhoneMediaSessionState.Stopped, "TEST", "test")
        };
        await scenario.Service.ConnectAsync("new", CancellationToken.None);
        Assert.Equal(["media.switch", "media.start.new"], scenario.Operations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplicitHistoricalConnectionHonorsAutomaticOpenWhenPreviousSessionWasStopped(bool enabled)
    {
        await using Scenario scenario = new() { AutoOpen = enabled, MediaSnapshot = new(PhoneMediaSessionState.Stopped, "TEST", "test") };
        await scenario.Service.ConnectAsync("new", CancellationToken.None);
        Assert.Equal(enabled ? "media.pause,connect,media.start.new" : "media.pause,connect", string.Join(',', scenario.Operations));
    }

    [Fact]
    public async Task ReadyConnectionWithAutomaticOpenOffDoesNotStartOrRestartAnOverlay()
    {
        await using Scenario scenario = new() { Available = true, MediaSnapshot = new(PhoneMediaSessionState.Stopped, "TEST", "test") };
        await scenario.Service.ConnectAsync("new", CancellationToken.None);
        Assert.Equal(["media.switch"], scenario.Operations);
    }

    [Fact]
    public async Task AlreadyRunningOverlayAndFailedConnectAreNotStartedAgain()
    {
        await using Scenario running = new() { Available = true, AutoOpen = true };
        await running.Service.ConnectAsync("new", CancellationToken.None);
        Assert.Equal(["media.switch"], running.Operations);
        await using Scenario failed = new() { FailConnect = true, AutoOpen = true, MediaSnapshot = new(PhoneMediaSessionState.Stopped, "TEST", "test") };
        await Assert.ThrowsAsync<AndroidConnectionException>(() => failed.Service.ConnectAsync("new", CancellationToken.None));
        Assert.Equal(["media.pause", "connect"], failed.Operations);
    }

    [Fact]
    public async Task IncompatibleDeviceCannotCloseTheWorkingSession()
    {
        await using Scenario scenario = new() { Method = PhoneConnectionMethod.Usb };
        await Assert.ThrowsAsync<AndroidConnectionException>(() => scenario.Service.ConnectAsync("new", CancellationToken.None));
        Assert.Empty(scenario.Operations);
    }

    private sealed class Scenario : IAndroidConnectionService, IAndroidConnectionManagementService, IPhoneMediaSessionCoordinator
    {
        public List<string> Operations { get; } = [];
        public bool FailConnect { get; init; }
        public bool NoTarget { get; init; }
        public bool Available { get; init; }
        public bool AutoOpen { get; init; }
        public PhoneConnectionMethod Method { get; set; } = PhoneConnectionMethod.WirelessCode;
        public PhoneMediaSessionSnapshot MediaSnapshot { get; set; } = new(PhoneMediaSessionState.Running, "TEST", "test");
        private AndroidConnectionSnapshot _connection = CreateSnapshot("old");
        public AndroidConnectionSnapshot Snapshot => Available ? _connection with
        { Devices = [new("new", "New", "New", AndroidDeviceStatus.Ready, AndroidTransport.Network, _connection.SelectedDevice?.DeviceKey == "new")] } : _connection;
        PhoneMediaSessionSnapshot IPhoneMediaSessionCoordinator.Snapshot => MediaSnapshot;
        public PhoneConnectionManagementService Service => new(this, this, this, () => new(), () => AutoOpen);
        public ConnectionManagementSnapshot ManagementSnapshot => new(Method,
        [new("new", "New", "New", AndroidTransport.Network, false, AndroidDeviceStatus.Offline, false, true, DateTimeOffset.UtcNow)]);
        public event EventHandler<AndroidConnectionChangedEventArgs>? StateChanged { add { } remove { } }
        event EventHandler<PhoneMediaSessionChangedEventArgs>? IPhoneMediaSessionCoordinator.StateChanged { add { } remove { } }
        public bool CanStart(string? deviceKey) => true;
        public void ConfigureAutoStart(bool enabled, AndroidVideoOptions videoOptions) { }
        public void RequestAutomaticStart() => Operations.Add("media.rearm");
        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RefreshAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask SelectDeviceAsync(string deviceKey, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask SwitchDeviceAsync(string deviceKey, Func<AndroidVideoOptions> videoOptions, CancellationToken cancellationToken)
        { Operations.Add("media.switch"); _connection = CreateSnapshot(deviceKey); return ValueTask.CompletedTask; }
        public ValueTask RestartScreenAsync(string? deviceKey, AndroidVideoOptions options, Func<CancellationToken, ValueTask>? whilePaused, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask StartAsync(string? deviceKey, AndroidVideoOptions options, CancellationToken cancellationToken)
        { Operations.Add("media.start." + deviceKey); MediaSnapshot = MediaSnapshot with { State = PhoneMediaSessionState.Running }; return ValueTask.CompletedTask; }
        public ValueTask StopAsync(CancellationToken cancellationToken)
        { Operations.Add("media.stop"); MediaSnapshot = MediaSnapshot with { State = PhoneMediaSessionState.Stopped }; return ValueTask.CompletedTask; }
        public ValueTask StopForConnectionChangeAsync(CancellationToken cancellationToken)
        { Operations.Add("media.pause"); MediaSnapshot = MediaSnapshot with { State = PhoneMediaSessionState.Stopped }; return ValueTask.CompletedTask; }
        public ValueTask SetConnectionMethodAsync(PhoneConnectionMethod method, CancellationToken cancellationToken)
        { Operations.Add("method"); Method = method; _connection = NoTarget ? CreateSnapshot("new") with { State = AndroidConnectionState.WaitingForDevice, SelectedDevice = null } : CreateSnapshot("new"); return ValueTask.CompletedTask; }
        public ValueTask SetConnectionMethodAsync(PhoneConnectionMethod method, bool preserveCurrentConnection, CancellationToken cancellationToken)
        {
            if (!preserveCurrentConnection) { return SetConnectionMethodAsync(method, cancellationToken); }
            Operations.Add("method"); Method = method; return ValueTask.CompletedTask;
        }
        public ValueTask SetDeviceAutoConnectAsync(string deviceKey, bool enabled, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RenameDeviceAsync(string deviceKey, string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ForgetDeviceAsync(string deviceKey, CancellationToken cancellationToken)
        { Operations.Add("forget"); return ValueTask.CompletedTask; }
        public ValueTask DisconnectSelectionAsync(CancellationToken cancellationToken)
        { Operations.Add("disconnect"); return ValueTask.CompletedTask; }
        public ValueTask ConnectKnownDeviceAsync(string deviceKey, CancellationToken cancellationToken)
        {
            Operations.Add("connect");
            if (FailConnect) { throw new AndroidConnectionException("TEST", "connect failed"); }
            _connection = CreateSnapshot(deviceKey);
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        private static AndroidConnectionSnapshot CreateSnapshot(string key) => new(1, AndroidConnectionState.Ready, "TEST", "test", [],
            new(key, "test", "test", "test", "test", "15", 35, "arm64", AndroidTransport.Network), DateTimeOffset.UtcNow, null);
    }
}
