using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class WirelessAdbProcessTests
{
    // Run the bundled client against an isolated loopback host-protocol stub.
    // No real ADB server, phone, SteamVR session, or network endpoint is contacted.
    [Theory]
    [InlineData(WirelessAdbOperation.Pair, "Successfully paired to 192.168.1.20:30001 [guid=test]", true)]
    [InlineData(WirelessAdbOperation.Pair, "Failed: Wrong password or connection was dropped.", false)]
    [InlineData(WirelessAdbOperation.Connect, "connected to 192.168.1.20:30001", true)]
    [InlineData(WirelessAdbOperation.Connect, "failed to connect to 192.168.1.20:30001", false)]
    [InlineData(WirelessAdbOperation.Disconnect, "disconnected 192.168.1.20:30001", true)]
    public async Task BundledClientUsesExpectedProtocolAndParsesActualConsoleOutput(
        WirelessAdbOperation operation, string response, bool success)
    {
        using CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(10));
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task<string> host = RespondAsync(listener, response, lifetime.Token);
        IsolatedRunner runner = new(port);
        await using WirelessAdbConnector connector = new(_ => ValueTask.FromResult<IAdbCommandRunner>(runner));
        AndroidOperationResult result = await connector.ExecuteAsync(operation,
            "192.168.1.20:30001", "123456", lifetime.Token);
        Assert.Equal(success, result.Succeeded);
        string request = await host;
        string expected = operation switch
        {
            WirelessAdbOperation.Pair => "host:pair:123456:192.168.1.20:30001",
            WirelessAdbOperation.Connect => "host:connect:192.168.1.20:30001",
            _ => "host:disconnect:192.168.1.20:30001",
        };
        Assert.Equal(expected, request);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullAuditBlockedRealClientTerminatesOnDeadlineOrCancellation(bool cancel)
    {
        using CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(10));
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        AdbCommandRunner runner = new(Path.Combine(AppContext.BaseDirectory,
            "resources", "android-platform-tools", "adb.exe"), killProcessTree: false);
        Task<AdbCommandResult> operation = runner.RunAsync(
            ["-H", "127.0.0.1", "-P", port.ToString(CultureInfo.InvariantCulture), "connect", "192.168.1.20:30001"],
            TimeSpan.FromSeconds(2), lifetime.Token).AsTask();
        using TcpClient peer = await listener.AcceptTcpClientAsync(lifetime.Token);
        await using NetworkStream stream = peer.GetStream();
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, lifetime.Token);
        int size = int.Parse(Encoding.ASCII.GetString(header), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte[] request = new byte[size];
        await stream.ReadExactlyAsync(request, lifetime.Token);
        // The isolated host deliberately withholds its response.
        if (cancel)
        {
            await lifetime.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        }
        else
        {
            AdbCommandResult result = await operation;
            Assert.True(result.TimedOut);
            Assert.False(result.Succeeded);
        }
        using CancellationTokenSource closedDeadline = new(TimeSpan.FromSeconds(2));
        try
        {
            Assert.Equal(0, await stream.ReadAsync(header, closedDeadline.Token));
        }
        catch (IOException exception) when (exception.InnerException is SocketException
        { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted })
        {
            // Windows may report process termination as RST instead of orderly EOF.
        }
    }

    private static async Task<string> RespondAsync(TcpListener listener, string response,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using NetworkStream stream = client.GetStream();
            byte[] length = new byte[4];
            await stream.ReadExactlyAsync(length, cancellationToken);
            int size = int.Parse(Encoding.ASCII.GetString(length), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Assert.InRange(size, 1, 1024);
            byte[] payload = new byte[size];
            await stream.ReadExactlyAsync(payload, cancellationToken);
            string request = Encoding.UTF8.GetString(payload);
            string body = request == "host:version" ? "0029" : response;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            byte[] header = Encoding.ASCII.GetBytes("OKAY" + bytes.Length.ToString("x4", CultureInfo.InvariantCulture));
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(bytes, cancellationToken);
            if (request != "host:version") { return request; }
        }
        throw new InvalidOperationException("No operation received by the isolated host stub.");
    }

    private sealed class IsolatedRunner(int port) : IAdbCommandRunner
    {
        private readonly AdbCommandRunner _runner = new(Path.Combine(AppContext.BaseDirectory,
            "resources", "android-platform-tools", "adb.exe"));

        public ValueTask<AdbCommandResult> RunAsync(IReadOnlyList<string> arguments,
            TimeSpan timeout, CancellationToken cancellationToken, string? standardInput = null) =>
            _runner.RunAsync(["-H", "127.0.0.1", "-P", port.ToString(CultureInfo.InvariantCulture), .. arguments],
                timeout, cancellationToken, standardInput);
    }
}
