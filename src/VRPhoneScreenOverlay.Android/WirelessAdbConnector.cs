namespace VRPhoneScreenOverlay.Android;

public enum WirelessAdbOperation
{
    Pair,
    Connect,
    Disconnect,
    PairQr,
}

public interface IAndroidWirelessConnectionService
{
    public ValueTask<AndroidOperationResult> ExecuteWirelessAsync(
        WirelessAdbOperation operation, string endpoint, string pairingCode,
        CancellationToken cancellationToken);
}

// One user operation at a time, zero pending requests (reject while busy).
// Pairing runs separately from discovery and the real-time media/control queues.
internal sealed class WirelessAdbConnector(
    Func<CancellationToken, ValueTask<IAdbCommandRunner>> createRunner, TimeSpan? qrLifetime = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<CancellationToken, ValueTask<IAdbCommandRunner>> _createRunner = createRunner;
    private IAdbCommandRunner? _runner;
    private bool _disposed;
    private int _waitingForQr;
    internal bool IsWaitingForQr => Volatile.Read(ref _waitingForQr) != 0;

    public async ValueTask<AndroidOperationResult> ExecuteAsync(
        WirelessAdbOperation operation, string endpoint, string pairingCode,
        CancellationToken cancellationToken, bool autoConnect = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        bool qr = operation == WirelessAdbOperation.PairQr;
        bool discover = operation == WirelessAdbOperation.Pair && string.IsNullOrWhiteSpace(endpoint);
        string normalized = string.Empty;
        if (!Enum.IsDefined(operation) || (!qr && !discover && !WirelessAdbEndpoint.TryNormalize(endpoint, out normalized)) ||
            (qr && (!endpoint.StartsWith("studio-", StringComparison.Ordinal) || endpoint.Length != 23 ||
                endpoint[7..].Any(character => !Uri.IsHexDigit(character)) || pairingCode.Length != 32 ||
                pairingCode.Any(character => !Uri.IsHexDigit(character)))))
        {
            return new(false, "WIRELESS_INVALID_ENDPOINT", "请输入手机显示的局域网 IP:端口；IPv6 使用 [地址]:端口");
        }
        if (operation == WirelessAdbOperation.Pair &&
            (pairingCode.Length != 6 || pairingCode.Any(character => character is < '0' or > '9')))
        {
            return new(false, "WIRELESS_INVALID_CODE", "请输入手机当前显示的六位配对码");
        }
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new(false, "WIRELESS_BUSY", "无线连接操作正在进行，请稍候");
        }
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _stop.Token);
        if (qr) { linked.CancelAfter(qrLifetime ?? WirelessPairingQr.RefreshInterval); Volatile.Write(ref _waitingForQr, 1); }
        try
        {
            _runner ??= await _createRunner(linked.Token).ConfigureAwait(false);
            if (qr || discover)
            {
                // QR discovery is bounded by its session deadline; code-only discovery retries briefly for newly advertised phones.
                for (int attempt = 0; qr || attempt < 3; attempt++)
                {
                    string[] candidates = await DiscoverAsync("_adb-tls-pairing._tcp", qr ? endpoint : null, null, linked.Token).ConfigureAwait(false);
                    if (candidates.Length > 1) { return new(false, "WIRELESS_AMBIGUOUS", "发现多台待配对手机，请手填目标手机的配对地址"); }
                    if (candidates.Length == 1) { normalized = candidates[0]; break; }
                    if (qr || attempt < 2) { await Task.Delay(TimeSpan.FromSeconds(qr ? 2 : 1), linked.Token).ConfigureAwait(false); }
                }
                if (normalized.Length == 0)
                {
                    return new(false, "WIRELESS_NOT_DISCOVERED", qr
                    ? "二维码已过期或未发现手机，请刷新二维码；若仍失败，请使用手动配对"
                    : "未发现配对地址，请保持手机配对码窗口打开，或手动填写其 IP 和配对端口");
                }
            }
            linked.Token.ThrowIfCancellationRequested();
            // Once the phone has scanned, allow its handshake to finish under
            // the normal command deadlines instead of expiring the QR mid-pair.
            if (qr) { Volatile.Write(ref _waitingForQr, 0); linked.CancelAfter(Timeout.InfiniteTimeSpan); }
            string command = operation switch
            {
                WirelessAdbOperation.Pair or WirelessAdbOperation.PairQr => "pair",
                WirelessAdbOperation.Connect => "connect",
                _ => "disconnect",
            };
            AdbCommandResult result = await _runner.RunAsync(
                [command, normalized], TimeSpan.FromSeconds(20), linked.Token,
                operation is WirelessAdbOperation.Pair or WirelessAdbOperation.PairQr ? pairingCode : null).ConfigureAwait(false);
            if (result.TimedOut)
            {
                return new(false, "WIRELESS_TIMEOUT", "操作超时：请检查同一局域网、无线调试开关及端口，再重试");
            }

            // adb connect may exit 0 while reporting a failed connection.
            string output = result.StandardOutput.Trim();
            const string pairingPrompt = "Enter pairing code:";
            if (operation is WirelessAdbOperation.Pair or WirelessAdbOperation.PairQr &&
                output.StartsWith(pairingPrompt, StringComparison.OrdinalIgnoreCase))
            {
                output = output[pairingPrompt.Length..].TrimStart();
            }
            bool accepted = result.Succeeded && operation switch
            {
                WirelessAdbOperation.Pair or WirelessAdbOperation.PairQr => output.StartsWith("Successfully paired to ", StringComparison.OrdinalIgnoreCase),
                WirelessAdbOperation.Connect => output.StartsWith("connected to ", StringComparison.OrdinalIgnoreCase) ||
                    output.StartsWith("already connected to ", StringComparison.OrdinalIgnoreCase),
                _ => output.StartsWith("disconnected ", StringComparison.OrdinalIgnoreCase),
            };
            if (!accepted)
            {
                return new(false, "WIRELESS_REJECTED", operation is WirelessAdbOperation.Pair or WirelessAdbOperation.PairQr
                    ? "配对失败：请重新打开配对码窗口，核对配对端口与六位码"
                    : "连接操作失败：请核对无线调试主页的连接端口、网络和授权状态");
            }

            if (autoConnect && operation is WirelessAdbOperation.Pair or WirelessAdbOperation.PairQr)
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    string[] connections = await DiscoverAsync("_adb-tls-connect._tcp", null,
                        normalized[..normalized.LastIndexOf(':')], linked.Token).ConfigureAwait(false);
                    if (connections.Length > 1) { break; }
                    if (connections.Length == 1)
                    {
                        AdbCommandResult connection = await _runner.RunAsync(["connect", connections[0]],
                            TimeSpan.FromSeconds(10), linked.Token).ConfigureAwait(false);
                        if (connection.Succeeded && (connection.StandardOutput.TrimStart().StartsWith("connected to ", StringComparison.OrdinalIgnoreCase) ||
                            connection.StandardOutput.TrimStart().StartsWith("already connected to ", StringComparison.OrdinalIgnoreCase)))
                        {
                            return new(true, "WIRELESS_CONNECTED", "配对及连接已完成，正在检查手机状态") { ConnectedEndpoint = connections[0] };
                        }
                        break;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(1), linked.Token).ConfigureAwait(false);
                }
                return new(true, "WIRELESS_PAIRED", "配对成功；自动连接未完成。请刷新设备，或填写无线调试主页的连接地址后点击连接");
            }
            return new(true, "WIRELESS_OK", operation switch
            {
                WirelessAdbOperation.Pair or WirelessAdbOperation.PairQr => "配对成功。请返回手机无线调试主页，填写连接端口后连接",
                WirelessAdbOperation.Connect => "无线连接已建立，正在检查设备授权与投屏能力",
                _ => "指定无线连接已断开",
            });
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return qr && !cancellationToken.IsCancellationRequested && !_stop.IsCancellationRequested
                ? new(false, "WIRELESS_QR_EXPIRED", "二维码已过期")
                : new(false, "WIRELESS_CANCELLED", "无线连接操作已取消");
        }
        catch (Exception exception) when (exception is AndroidConnectionException or IOException or
            UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new(false, "WIRELESS_TOOL_FAILED", "无线连接组件暂不可用，请刷新设备后重试");
        }
        finally
        {
            Volatile.Write(ref _waitingForQr, 0);
            _gate.Release();
        }
    }

    private async ValueTask<string[]> DiscoverAsync(string type, string? name, string? host, CancellationToken cancellationToken)
    {
        AdbCommandResult result = await _runner!.RunAsync(["mdns", "services"], TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? WirelessAdbEndpoint.FindServices(result.StandardOutput, type, name, host) : [];
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;
        await _stop.CancelAsync().ConfigureAwait(false);
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        _gate.Release();
        _gate.Dispose();
        _stop.Dispose();
    }
}
