using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace VRPhoneScreenOverlay.Android;

internal static class AdbServerCompatibilityProbe
{
    // The bundled tool reports Android Debug Bridge 1.0.41. A mismatched server
    // can be killed automatically by adb itself; inspect without invoking adb.
    internal const int ExpectedVersion = 41;

    internal static async ValueTask<int?> ReadVersionAsync(AdbEndpoint endpoint, CancellationToken token)
    {
        if (endpoint.Host is null || endpoint.Diagnostic.ServerPort is not int port) { return null; }
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Windows can take about two seconds to return ConnectionRefused even
        // on loopback. Keep startup possible while the outer command deadline
        // still bounds this entire probe and subsequent tool execution.
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using TcpClient client = new();
        try
        {
            await client.ConnectAsync(endpoint.Host, port, deadline.Token).ConfigureAwait(false);
            deadline.CancelAfter(TimeSpan.FromSeconds(1));
            await using NetworkStream stream = client.GetStream();
            await stream.WriteAsync("000chost:version"u8.ToArray(), deadline.Token).ConfigureAwait(false);
            byte[] header = new byte[4];
            await stream.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            if (!header.AsSpan().SequenceEqual("OKAY"u8)) { throw new InvalidDataException("Unexpected ADB probe status."); }
            await stream.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            if (!int.TryParse(Encoding.ASCII.GetString(header), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int length) || length is < 1 or > 8)
            { throw new InvalidDataException("Unexpected ADB probe length."); }
            byte[] value = new byte[length];
            await stream.ReadExactlyAsync(value, deadline.Token).ConfigureAwait(false);
            if (!int.TryParse(Encoding.ASCII.GetString(value), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int version))
            { throw new InvalidDataException("Invalid ADB version."); }
            return version;
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused) { return null; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new AndroidConnectionException("ANDROID_SERVER_PROBE_TIMEOUT", "ADB 服务状态探测超时，未重启共享服务"); }
        catch (Exception exception) when (exception is SocketException or IOException)
        { throw new AndroidConnectionException("ANDROID_SERVER_PROBE_FAILED", "无法确认 ADB 服务状态，未重启共享服务", exception); }
    }
}
