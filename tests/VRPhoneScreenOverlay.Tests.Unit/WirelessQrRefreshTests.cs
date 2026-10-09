using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.App;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class WirelessQrRefreshTests
{
    [Fact]
    public void DefaultValidityIsFiveMinutesAndNetworkTransportIsSupported()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), WirelessPairingQr.Lifetime);
        Assert.Equal(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(20), WirelessPairingQr.RefreshInterval);
        Assert.Equal("无线", AndroidDisplayNames.Transport(AndroidTransport.Network));
    }

    [Fact]
    public async Task ContinuousDisplayGeneratesFreshCredentialsAfterSuccessAndStopsOnNavigation()
    {
        SequenceService service = new(["WIRELESS_READY"]);
        List<WirelessPairingQr> shown = [];
        List<AndroidOperationResult> results = [];
        AndroidOperationResult final = await WirelessQrPairingFlow.RunContinuousAsync(service, (qr, _) => shown.Add(qr),
            () => service.Calls == 0, results.Add, CancellationToken.None);
        Assert.Equal("WIRELESS_CANCELLED", final.ReasonCode);
        Assert.Single(shown);
        Assert.True(Assert.Single(results).Succeeded);
    }

    [Fact]
    public async Task ExpiryRenewsCredentialsSeriallyAndSuccessStopsRefreshing()
    {
        SequenceService service = new(["WIRELESS_QR_EXPIRED", "WIRELESS_QR_EXPIRED", "WIRELESS_READY"]);
        List<(WirelessPairingQr Qr, bool Refreshed)> shown = [];
        var result = await WirelessQrPairingFlow.RunAsync(service, (qr, refreshed) => shown.Add((qr, refreshed)),
            () => true, CancellationToken.None);
        Assert.Equal("WIRELESS_READY", result.ReasonCode);
        Assert.Equal(3, service.Calls);
        Assert.Equal(3, shown.Select(item => item.Qr.ServiceName).Distinct().Count());
        Assert.Equal(3, shown.Select(item => item.Qr.Password).Distinct().Count());
        Assert.Equal([false, true, true], shown.Select(item => item.Refreshed));
    }

    [Theory]
    [InlineData("WIRELESS_CANCELLED")]
    [InlineData("WIRELESS_REJECTED")]
    [InlineData("WIRELESS_TIMEOUT")]
    [InlineData("WIRELESS_TOOL_FAILED")]
    public async Task ActualFailuresAndCancellationNeverRestartPairing(string reason)
    {
        SequenceService service = new([reason]);
        var result = await WirelessQrPairingFlow.RunAsync(service, (_, _) => { }, () => true, CancellationToken.None);
        Assert.Equal(reason, result.ReasonCode);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task LeavingThePageAfterExpiryDoesNotCreateAnotherQr()
    {
        SequenceService service = new(["WIRELESS_QR_EXPIRED"]);
        int shown = 0;
        var result = await WirelessQrPairingFlow.RunAsync(service, (_, _) => shown++, () => service.Calls == 0, CancellationToken.None);
        Assert.Equal("WIRELESS_CANCELLED", result.ReasonCode);
        Assert.Equal(1, shown);
    }

    [Fact]
    public async Task CancellingDuringRenewalDoesNotStartASecondTransaction()
    {
        using CancellationTokenSource cancellation = new();
        SequenceService service = new(["WIRELESS_QR_EXPIRED"]);
        var operation = WirelessQrPairingFlow.RunAsync(service, (_, _) => { }, () => true, cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task ConnectorDeadlineProducesAnExpiryAndReleasesItsGate()
    {
        await using WirelessAdbConnector connector = new(_ => ValueTask.FromResult<IAdbCommandRunner>(new WaitingRunner()),
            qrLifetime: TimeSpan.FromMilliseconds(25));
        for (int attempt = 0; attempt < 2; attempt++)
        {
            WirelessPairingQr qr = new();
            var result = await connector.ExecuteAsync(WirelessAdbOperation.PairQr, qr.ServiceName, qr.Password, CancellationToken.None);
            Assert.Equal("WIRELESS_QR_EXPIRED", result.ReasonCode);
        }
    }

    [Fact]
    public async Task ScannedPhoneCanFinishPairingAfterTheDisplayDeadline()
    {
        WirelessPairingQr qr = new();
        await using WirelessAdbConnector connector = new(_ => ValueTask.FromResult<IAdbCommandRunner>(new ScannedRunner(qr.ServiceName)),
            qrLifetime: TimeSpan.FromSeconds(1));
        var result = await connector.ExecuteAsync(WirelessAdbOperation.PairQr, qr.ServiceName, qr.Password, CancellationToken.None);
        Assert.True(result.Succeeded);
    }

    private sealed class ScannedRunner(string serviceName) : IAdbCommandRunner
    {
        public async ValueTask<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, string? standardInput = null)
        {
            if (arguments[0] == "mdns")
            {
                return new(0, serviceName + " _adb-tls-pairing._tcp 192.168.1.20:30000", "", TimeSpan.Zero, false);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(1100), cancellationToken);
            return new(0, "Successfully paired to 192.168.1.20:30000", "", TimeSpan.Zero, false);
        }
    }

    private sealed class SequenceService(string[] reasons) : IAndroidWirelessConnectionService
    {
        public int Calls { get; private set; }
        public ValueTask<AndroidOperationResult> ExecuteWirelessAsync(WirelessAdbOperation operation, string endpoint,
            string pairingCode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(WirelessAdbOperation.PairQr, operation);
            string reason = reasons[Calls++];
            return ValueTask.FromResult(new AndroidOperationResult(reason == "WIRELESS_READY", reason, "test"));
        }
    }

    private sealed class WaitingRunner : IAdbCommandRunner
    {
        public async ValueTask<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, string? standardInput = null)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation was expected.");
        }
    }
}
