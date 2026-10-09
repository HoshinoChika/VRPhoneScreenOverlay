using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class WirelessAdbTests
{
    [Theory]
    [InlineData("192.168.1.20:30001", "adb-example._adb-tls-connect._tcp")]
    [InlineData("192.168.1.20:30000", null)]
    [InlineData("192.168.1.21:30001", null)]
    public void MdnsMappingRequiresMatchingConnectServiceAddressAndPort(string endpoint, string? expected)
    {
        const string output = """
            List of discovered mdns services
            adb-pairing  _adb-tls-pairing._tcp  192.168.1.20:30000
            adb-example _adb-tls-connect._tcp. 192.168.1.20:30001
            adb-legacy  _adb._tcp 192.168.1.21:30001
            """;
        Assert.Equal(expected, WirelessAdbEndpoint.FindConnectService(output, endpoint));
    }

    [Theory]
    [InlineData("192.168.1.20:37123", true)]
    [InlineData("10.1.2.3:65535", true)]
    [InlineData("172.16.1.2:1", true)]
    [InlineData("[fd00::123]:12345", true)]
    [InlineData("[fe80::1%12]:12345", true)]
    [InlineData("8.8.8.8:5555", false)]
    [InlineData("127.0.0.1:5037", false)]
    [InlineData("[::1]:5037", false)]
    [InlineData("192.168.1.2", false)]
    [InlineData("192.168.1.2:0", false)]
    [InlineData("192.168.1.2:65536", false)]
    [InlineData("192.168.1.2:5555 shell", false)]
    [InlineData("-s device", false)]
    [InlineData("example.com:5555", false)]
    [InlineData("fd00::1:1234", false)]
    public void EndpointsRequireExplicitLanAddressAndPort(string value, bool valid) =>
        Assert.Equal(valid, WirelessAdbEndpoint.TryNormalize(value, out _));

    [Theory]
    [InlineData("")]
    [InlineData("Enter pairing code: ")]
    [InlineData("Enter pairing code: \r\n")]
    public async Task PairCodeIsSentOnlyThroughStandardInput(string prompt)
    {
        FakeRunner runner = new(prompt + "Successfully paired to 192.168.1.20:30000 [guid=test]");
        await using WirelessAdbConnector connector = Create(runner);
        AndroidOperationResult result = await connector.ExecuteAsync(
            WirelessAdbOperation.Pair, "192.168.1.20:30000", "123456", CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(["pair", "192.168.1.20:30000"], runner.Arguments);
        Assert.Equal("123456", runner.StandardInput);
        Assert.DoesNotContain("123456", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168", result.Message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromSeconds(20), runner.Timeout);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("１２３４５６")]
    [InlineData("12 456")]
    public async Task InvalidPairCodeDoesNotLaunchProcess(string code)
    {
        FakeRunner runner = new("");
        await using WirelessAdbConnector connector = Create(runner);
        AndroidOperationResult result = await connector.ExecuteAsync(
            WirelessAdbOperation.Pair, "192.168.1.20:30000", code, CancellationToken.None);
        Assert.Equal("WIRELESS_INVALID_CODE", result.ReasonCode);
        Assert.Null(runner.Arguments);
    }

    [Theory]
    [InlineData("connected to 192.168.1.20:30001", true)]
    [InlineData("already connected to 192.168.1.20:30001", true)]
    [InlineData("failed to connect to 192.168.1.20:30001", false)]
    [InlineData("cannot connect to 192.168.1.20:30001: refused", false)]
    public async Task ConnectChecksOutputEvenWhenExitCodeIsZero(string output, bool success)
    {
        FakeRunner runner = new(output);
        await using WirelessAdbConnector connector = Create(runner);
        AndroidOperationResult result = await connector.ExecuteAsync(
            WirelessAdbOperation.Connect, "192.168.1.20:30001", "", CancellationToken.None);
        Assert.Equal(success, result.Succeeded);
        Assert.Null(runner.StandardInput);
    }

    [Fact]
    public async Task DisconnectTargetsOnlyTheSpecifiedAddress()
    {
        FakeRunner runner = new("disconnected 192.168.1.20:30001");
        await using WirelessAdbConnector connector = Create(runner);
        Assert.True((await connector.ExecuteAsync(WirelessAdbOperation.Disconnect,
            "192.168.1.20:30001", "", CancellationToken.None)).Succeeded);
        Assert.Equal(["disconnect", "192.168.1.20:30001"], runner.Arguments);
    }

    [Fact]
    public async Task TimeoutIsReportedWithoutRawOutput()
    {
        FakeRunner runner = new("private output") { TimedOut = true };
        await using WirelessAdbConnector connector = Create(runner);
        AndroidOperationResult result = await connector.ExecuteAsync(
            WirelessAdbOperation.Connect, "192.168.1.20:30001", "", CancellationToken.None);
        Assert.Equal("WIRELESS_TIMEOUT", result.ReasonCode);
        Assert.DoesNotContain("private", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BusyOperationIsRejectedAndCancellationAllowsRetry()
    {
        FakeRunner runner = new("connected to 192.168.1.20:30001") { Wait = true };
        await using WirelessAdbConnector connector = Create(runner);
        using CancellationTokenSource cancellation = new();
        Task<AndroidOperationResult> pending = connector.ExecuteAsync(WirelessAdbOperation.Connect,
            "192.168.1.20:30001", "", cancellation.Token).AsTask();
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        AndroidOperationResult busy = await connector.ExecuteAsync(WirelessAdbOperation.Pair,
            "192.168.1.20:30000", "123456", CancellationToken.None);
        Assert.Equal("WIRELESS_BUSY", busy.ReasonCode);
        await cancellation.CancelAsync();
        Assert.Equal("WIRELESS_CANCELLED", (await pending).ReasonCode);
        runner.Wait = false;
        Assert.True((await connector.ExecuteAsync(WirelessAdbOperation.Connect,
            "192.168.1.20:30001", "", CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task DisposeCancelsOutstandingOperation()
    {
        FakeRunner runner = new("") { Wait = true };
        await using WirelessAdbConnector connector = Create(runner);
        Task<AndroidOperationResult> pending = connector.ExecuteAsync(WirelessAdbOperation.Connect,
            "192.168.1.20:30001", "", CancellationToken.None).AsTask();
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await connector.DisposeAsync();
        Assert.Equal("WIRELESS_CANCELLED", (await pending).ReasonCode);
    }

    private static WirelessAdbConnector Create(FakeRunner runner) =>
        new(_ => ValueTask.FromResult<IAdbCommandRunner>(runner));

    private sealed class FakeRunner(string output) : IAdbCommandRunner
    {
        public IReadOnlyList<string>? Arguments { get; private set; }
        public string? StandardInput { get; private set; }
        public TimeSpan Timeout { get; private set; }
        public bool TimedOut { get; init; }
        public bool Wait { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken cancellationToken, string? standardInput = null)
        {
            Arguments = arguments;
            StandardInput = standardInput;
            Timeout = timeout;
            Started.TrySetResult();
            if (Wait) { await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken); }
            return new(0, output, "", TimeSpan.Zero, TimedOut);
        }
    }
}
