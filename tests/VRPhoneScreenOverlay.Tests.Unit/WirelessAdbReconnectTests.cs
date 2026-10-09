using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class WirelessAdbReconnectTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vrpso-reconnect-" + Guid.NewGuid().ToString("N"));
    private string Cache => Path.Combine(_directory, "endpoint.txt");
    private static AdbListResult Devices(string rows, string? mdns = null) =>
        new(AdbOutputParser.ParseDevices("List of devices attached\n" + rows), TimeSpan.Zero, "test", mdns);
    private WirelessAdbReconnect Create(Runner runner) => new(Cache, _ => ValueTask.FromResult<IAdbCommandRunner>(runner));

    [Fact]
    public async Task RestartWithoutMdnsUsesRememberedAuthorizedAddressAndBoundsRetryRate()
    {
        Runner runner = new();
        await Create(runner).ObserveAsync(Devices("192.168.1.20:30123 device model:Test"), CancellationToken.None, 0);
        Assert.Empty(runner.Commands);
        WirelessAdbReconnect restarted = Create(runner);
        Assert.True(await restarted.ObserveAsync(Devices(""), CancellationToken.None, 100));
        Assert.Equal("connect 192.168.1.20:30123", Assert.Single(runner.Commands));
        Assert.False(await restarted.ObserveAsync(Devices(""), CancellationToken.None, 101));
        Assert.True(await restarted.ObserveAsync(Devices(""), CancellationToken.None, 10100));
        Assert.Equal(2, runner.Commands.Count);
        Assert.Equal(TimeSpan.FromSeconds(3), runner.LastTimeout);
    }

    [Fact]
    public async Task ServiceNameLearnsOnlyItsMatchingConnectEndpoint()
    {
        Runner runner = new();
        await Create(runner).ObserveAsync(Devices("adb-target._adb-tls-connect._tcp device model:Test",
            "adb-other _adb-tls-connect._tcp 192.168.1.30:40123\nadb-target _adb-tls-connect._tcp 192.168.1.20:30123"), CancellationToken.None);
        Assert.Equal("192.168.1.20:30123", await File.ReadAllTextAsync(Cache));
    }

    [Fact]
    public async Task ManualDisconnectForgetsAddressAndDoesNotLearnItAgainUntilUserConnects()
    {
        Runner runner = new();
        WirelessAdbReconnect reconnect = Create(runner);
        await reconnect.ObserveAsync(Devices("192.168.1.20:30123 device"), CancellationToken.None);
        await reconnect.ForgetAsync(CancellationToken.None);
        await reconnect.ObserveAsync(Devices("192.168.1.20:30123 device"), CancellationToken.None);
        Assert.False(await reconnect.ObserveAsync(Devices(""), CancellationToken.None));
        Assert.False(await Create(runner).ObserveAsync(Devices(""), CancellationToken.None));
        Assert.Empty(runner.Commands);
        reconnect.Resume();
        await reconnect.ObserveAsync(Devices("192.168.1.20:30123 device"), CancellationToken.None);
        Assert.True(await reconnect.ObserveAsync(Devices(""), CancellationToken.None));
    }

    [Theory]
    [InlineData("usb-serial device")]
    [InlineData("192.168.1.20:30123 unauthorized")]
    public async Task ReadyOrUnauthorizedDevicePreventsAutomaticConnection(string rows)
    {
        Runner runner = new();
        WirelessAdbReconnect reconnect = Create(runner);
        await reconnect.ObserveAsync(Devices("192.168.1.20:30123 device"), CancellationToken.None);
        Assert.False(await reconnect.ObserveAsync(Devices(rows), CancellationToken.None));
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task CorruptOrPublicAddressIsNotUsedAndCancellationPropagates()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Cache, "8.8.8.8:5555");
        Runner runner = new();
        Assert.False(await Create(runner).ObserveAsync(Devices(""), CancellationToken.None));
        Assert.Empty(runner.Commands);
        await File.WriteAllTextAsync(Cache, "192.168.1.20:30123");
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(runner).ObserveAsync(Devices(""), cancelled.Token).AsTask());
    }

    private sealed class Runner : IAdbCommandRunner
    {
        public List<string> Commands { get; } = [];
        public TimeSpan LastTimeout { get; private set; }
        public ValueTask<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, string? standardInput = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(string.Join(" ", arguments));
            LastTimeout = timeout;
            return ValueTask.FromResult(new AdbCommandResult(0, "connected to test", "", TimeSpan.Zero, false));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) { Directory.Delete(_directory, recursive: true); }
    }
}
