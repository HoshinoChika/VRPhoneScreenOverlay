using System.ComponentModel;
using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidConnectionAuditTests
{
    [Fact]
    public async Task FullAuditFailedProbeMustNotReportWirelessReady()
    {
        FakeClient client = new() { ProbeSucceeds = false };
        await using AndroidConnectionService service = Create(client, new FakeRunner(), new FakeShutdown());
        AndroidOperationResult result = await service.ExecuteWirelessAsync(WirelessAdbOperation.Connect,
            "192.168.1.20:37001", "", CancellationToken.None);
        Assert.False(service.Snapshot.IsReady);
        Assert.False(result.Succeeded);
        Assert.Equal("WIRELESS_NOT_READY", result.ReasonCode);
    }

    [Fact]
    public async Task FullAuditNativeToolFailureRemainsRecoverable()
    {
        FakeClient client = new() { ScanFailure = new Win32Exception(5) };
        FakeShutdown shutdown = new();
        await using AndroidConnectionService service = Create(client, new FakeRunner(), shutdown);
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.Equal(AndroidConnectionState.Faulted, service.Snapshot.State);
        client.ScanFailure = null;
        await service.ScanOnceForTestAsync(CancellationToken.None);
        Assert.True(service.Snapshot.IsReady);
        await service.DisposeAsync();
        Assert.Equal(1, shutdown.Calls);
    }

    [Fact]
    public async Task FullAuditBusyIncludesPostConnectServiceResolution()
    {
        FakeClient client = new() { UseServiceName = true, BlockResolution = true };
        FakeRunner runner = new();
        await using AndroidConnectionService service = Create(client, runner, new FakeShutdown());
        using CancellationTokenSource lifetime = new();
        Task<AndroidOperationResult> pending = service.ExecuteWirelessAsync(WirelessAdbOperation.Connect,
            "192.168.1.20:37001", "", lifetime.Token).AsTask();
        try
        {
            await client.ResolutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            AndroidOperationResult busy = await service.ExecuteWirelessAsync(WirelessAdbOperation.Pair,
                "192.168.1.20:37002", "123456", CancellationToken.None);
            Assert.Equal("WIRELESS_BUSY", busy.ReasonCode);
            Assert.Equal(1, runner.Calls);
        }
        finally
        {
            await lifetime.CancelAsync();
            await ObserveCancellationAsync(pending);
        }
    }

    [Fact]
    public async Task FullAuditDisposeCancelsAndDrainsServiceResolutionBeforeServerShutdown()
    {
        FakeClient client = new() { UseServiceName = true, BlockResolution = true };
        FakeShutdown shutdown = new();
        await using AndroidConnectionService service = Create(client, new FakeRunner(), shutdown);
        using CancellationTokenSource lifetime = new();
        Task<AndroidOperationResult> pending = service.ExecuteWirelessAsync(WirelessAdbOperation.Connect,
            "192.168.1.20:37001", "", lifetime.Token).AsTask();
        try
        {
            await client.ResolutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await service.DisposeAsync();
            Assert.True(client.ResolutionFinished);
            Assert.True(pending.IsCompleted);
            Assert.Equal("WIRELESS_CANCELLED", (await pending).ReasonCode);
            Assert.Equal(1, shutdown.Calls);
            Assert.Equal(AndroidConnectionState.Stopped, service.Snapshot.State);
        }
        finally
        {
            await lifetime.CancelAsync();
            await ObserveCancellationAsync(pending);
        }
    }

    [Fact]
    public async Task FullAuditPreCancelledCommandNeverAttemptsProcessStart()
    {
        AdbCommandRunner runner = new(Path.Combine(AppContext.BaseDirectory, "not-an-adb-tool.exe"));
        using CancellationTokenSource lifetime = new();
        await lifetime.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            ["version"], TimeSpan.FromSeconds(1), lifetime.Token).AsTask());
    }

    private static async Task ObserveCancellationAsync(Task<AndroidOperationResult> pending)
    {
        try { await pending; }
        catch (OperationCanceledException) { }
    }

    private static AndroidConnectionService Create(FakeClient client, FakeRunner runner, FakeShutdown shutdown)
    {
        AndroidConnectionService service = new(AppContext.BaseDirectory, NullAndroidConnectionLog.Instance,
            new AndroidConnectionServiceOptions(), shutdown, _ => ValueTask.FromResult<IAdbCommandRunner>(runner));
        service.SetClientForTest(client);
        return service;
    }

    private sealed class FakeRunner : IAdbCommandRunner
    {
        public int Calls { get; private set; }
        public ValueTask<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, string? standardInput = null)
        {
            Calls++;
            return ValueTask.FromResult(new AdbCommandResult(0, "connected to " + arguments[1], "", TimeSpan.Zero, false));
        }
    }

    private sealed class FakeShutdown : IAdbServerShutdown
    {
        public int Calls { get; private set; }
        public ValueTask<AdbServerShutdownResult> StopAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new AdbServerShutdownResult(true, AndroidReasonCodes.AdbServerStopped, "stopped"));
        }
    }

    private sealed class FakeClient : IAdbClient
    {
        public bool ProbeSucceeds { get; init; } = true;
        public bool UseServiceName { get; init; }
        public bool BlockResolution { get; init; }
        public bool ResolutionFinished { get; private set; }
        public Exception? ScanFailure { get; set; }
        public TaskCompletionSource ResolutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<AdbListResult> ListDevicesAsync(CancellationToken cancellationToken)
        {
            if (ScanFailure is not null) { return ValueTask.FromException<AdbListResult>(ScanFailure); }
            string serial = UseServiceName ? "adb-example._adb-tls-connect._tcp" : "192.168.1.20:37001";
            return ValueTask.FromResult(new AdbListResult(
                AdbOutputParser.ParseDevices($"{serial} device model:Example"), TimeSpan.Zero, "test"));
        }

        public ValueTask<AdbProbeResult> ProbeAsync(string serial, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AdbProbeResult(ProbeSucceeds, "TEST_PROBE", "test", "Example", "Example",
                "Example", "example", "15", 35, "arm64-v8a", 1080, 2400, TimeSpan.Zero));

        public async ValueTask<string?> ResolveWirelessServiceAsync(string endpoint, CancellationToken cancellationToken)
        {
            ResolutionStarted.TrySetResult();
            try
            {
                if (BlockResolution) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                return UseServiceName ? "adb-example._adb-tls-connect._tcp" : null;
            }
            finally { ResolutionFinished = true; }
        }
    }
}
