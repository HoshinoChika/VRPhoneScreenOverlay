using System.ComponentModel;
using System.Net.Sockets;

namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyTransportLease : IAsyncDisposable
{
    private readonly string _serial;
    private readonly int _localPort;
    private readonly TcpClient _tcpClient;
    private readonly AdbManagedProcess _serverProcess;
    private readonly AdbCommandRunner _commandRunner;
    private readonly IAndroidConnectionLogSink _log;
    private readonly ScrcpySessionKind _kind;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private int _disposeRequested;

    public ScrcpyTransportLease(
        ResolvedAndroidDevice device,
        ScrcpySessionKind kind,
        int localPort,
        TcpClient tcpClient,
        AdbManagedProcess serverProcess,
        AdbCommandRunner commandRunner,
        IAndroidConnectionLogSink log)
    {
        DeviceKey = device.DeviceKey;
        _serial = device.Serial;
        _kind = kind;
        _localPort = localPort;
        _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        _serverProcess = serverProcess ?? throw new ArgumentNullException(nameof(serverProcess));
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public string DeviceKey { get; }

    public NetworkStream Stream
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            return _tcpClient.GetStream();
        }
    }

    public bool HasServerExited
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
            return _serverProcess.HasExited;
        }
    }

    public async ValueTask<AdbCommandResult> RunDeviceCommandAsync(
        IReadOnlyList<string> deviceArguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
        ArgumentNullException.ThrowIfNull(deviceArguments);
        List<string> arguments = new(deviceArguments.Count + 2)
        {
            "-s",
            _serial,
        };
        arguments.AddRange(deviceArguments);
        return await _commandRunner.RunAsync(arguments, timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_disposeGate)
        {
            _disposeTask ??= DisposeCoreAsync();
            disposeTask = _disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposeRequested, 1);
        _tcpClient.Dispose();
        try
        {
            await _serverProcess.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedCleanupFailure(exception))
        {
            WriteCleanupFailure("scrcpy_server_cleanup_failed", "SERVER_CLEANUP");
        }

        try
        {
            await _serverProcess.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedCleanupFailure(exception))
        {
            WriteCleanupFailure("scrcpy_server_cleanup_failed", "SERVER_CLEANUP");
        }

        try
        {
            AdbCommandResult result = await _commandRunner.RunAsync(
                    ["-s", _serial, "forward", "--remove", $"tcp:{_localPort}"],
                    TimeSpan.FromSeconds(3),
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                WriteCleanupFailure("scrcpy_forward_cleanup_failed", "FORWARD_CLEANUP");
            }
        }
        catch (Exception exception) when (IsExpectedCleanupFailure(exception))
        {
            WriteCleanupFailure("scrcpy_forward_cleanup_failed", "FORWARD_CLEANUP");
        }
    }

    private static bool IsExpectedCleanupFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or UnauthorizedAccessException or
            Win32Exception;

    private void WriteCleanupFailure(string eventName, string reasonSuffix)
    {
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            eventName,
            $"{ScrcpySessionLauncher.ReasonCodePrefix(_kind)}_{reasonSuffix}_FAILED",
            $"手机{ScrcpySessionLauncher.ChannelName(_kind)}会话清理未完全成功",
            DeviceKey));
    }
}
