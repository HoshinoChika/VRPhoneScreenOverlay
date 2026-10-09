using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class WirelessPairingAutomationTests
{
    [Fact]
    public void QrRefreshUsesIndependentCredentialsAndDoesNotExposeThemInToString()
    {
        WirelessPairingQr first = new();
        WirelessPairingQr second = new();
        Assert.NotEqual(first.ServiceName, second.ServiceName);
        Assert.NotEqual(first.Password, second.Password);
        Assert.Equal($"WIFI:T:ADB;S:{first.ServiceName};P:{first.Password};;", first.Payload);
        Assert.DoesNotContain(first.Password, first.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PairingDiscoversAddressAndConnectsOnlyThePairedHost(bool qr)
    {
        WirelessPairingQr session = new();
        Runner runner = new($"{session.ServiceName} _adb-tls-pairing._tcp 192.168.1.20:30000\n" +
            "other _adb-tls-connect._tcp 192.168.1.21:30001\n" +
            "target _adb-tls-connect._tcp 192.168.1.20:30002");
        await using WirelessAdbConnector connector = Create(runner);
        AndroidOperationResult result = await connector.ExecuteAsync(qr ? WirelessAdbOperation.PairQr : WirelessAdbOperation.Pair,
            qr ? session.ServiceName : "", qr ? session.Password : "123456", CancellationToken.None, autoConnect: true);
        Assert.True(result.Succeeded);
        Assert.Equal("192.168.1.20:30002", result.ConnectedEndpoint);
        Assert.Equal(["mdns", "pair", "mdns", "connect"], runner.Commands.Select(command => command[0]));
        Assert.Equal("192.168.1.20:30000", runner.Commands[1][1]);
        Assert.Equal("192.168.1.20:30002", runner.Commands[3][1]);
        Assert.DoesNotContain(session.Password, string.Join(' ', runner.Commands.SelectMany(command => command)), StringComparison.Ordinal);
        Assert.DoesNotContain("192.168", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualDiscoveryNeverGuessesBetweenPhones()
    {
        Runner runner = new("a _adb-tls-pairing._tcp 192.168.1.20:30000\nb _adb-tls-pairing._tcp 192.168.1.21:30000");
        await using WirelessAdbConnector connector = Create(runner);
        var result = await connector.ExecuteAsync(WirelessAdbOperation.Pair, "", "123456", CancellationToken.None, true);
        Assert.Equal("WIRELESS_AMBIGUOUS", result.ReasonCode);
        Assert.Single(runner.Commands);
    }

    [Fact]
    public async Task AmbiguousConnectServicesLeavePairingSuccessfulWithoutConnecting()
    {
        Runner runner = new("a _adb-tls-connect._tcp 192.168.1.20:30001\nb _adb-tls-connect._tcp 192.168.1.20:30002");
        await using WirelessAdbConnector connector = Create(runner);
        var result = await connector.ExecuteAsync(WirelessAdbOperation.Pair, "192.168.1.20:30000", "123456", CancellationToken.None, true);
        Assert.Equal("WIRELESS_PAIRED", result.ReasonCode);
        Assert.Null(result.ConnectedEndpoint);
        Assert.DoesNotContain(runner.Commands, command => command[0] == "connect");
    }

    [Fact]
    public async Task RefreshCancellationStopsOldQrAndNewSessionIgnoresOldAdvertisement()
    {
        WirelessPairingQr old = new();
        WirelessPairingQr current = new();
        Runner runner = new($"{old.ServiceName} _adb-tls-pairing._tcp 192.168.1.20:30000");
        await using WirelessAdbConnector connector = Create(runner);
        using CancellationTokenSource cancellation = new();
        var pending = connector.ExecuteAsync(WirelessAdbOperation.PairQr, current.ServiceName, current.Password, cancellation.Token, true).AsTask();
        await runner.Discovered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellation.CancelAsync();
        Assert.Equal("WIRELESS_CANCELLED", (await pending).ReasonCode);
        Assert.DoesNotContain(runner.Commands, command => command[0] == "pair");
        runner.Services = $"{current.ServiceName} _adb-tls-pairing._tcp 192.168.1.20:30000\na _adb-tls-connect._tcp 192.168.1.20:30002";
        Assert.True((await connector.ExecuteAsync(WirelessAdbOperation.PairQr, current.ServiceName, current.Password, CancellationToken.None, true)).Succeeded);
    }

    [Fact]
    public void DiscoveryRejectsWrongServicePublicAddressesAndDuplicates()
    {
        const string services = "a _adb-tls-pairing._tcp 192.168.1.20:1\na _adb-tls-pairing._tcp. 192.168.1.20:1\n" +
            "a _adb-tls-pairing._tcp 8.8.8.8:1\na _adb-tls-connect._tcp 192.168.1.20:2";
        Assert.Equal(["192.168.1.20:1"], WirelessAdbEndpoint.FindServices(services, "_adb-tls-pairing._tcp", "a", null));
    }

    private static WirelessAdbConnector Create(Runner runner) => new(_ => ValueTask.FromResult<IAdbCommandRunner>(runner));

    private sealed class Runner(string services) : IAdbCommandRunner
    {
        public string Services { get; set; } = services;
        public List<string[]> Commands { get; } = [];
        public TaskCompletionSource Discovered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken, string? standardInput = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(arguments.ToArray());
            string output = arguments[0] switch
            {
                "mdns" => Services,
                "pair" => "Enter pairing code: Successfully paired to " + arguments[1],
                "connect" => "connected to " + arguments[1],
                _ => throw new InvalidOperationException(),
            };
            Discovered.TrySetResult();
            return ValueTask.FromResult(new AdbCommandResult(0, output, "", TimeSpan.Zero, false));
        }
    }
}
