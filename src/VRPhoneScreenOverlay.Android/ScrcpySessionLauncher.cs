using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal enum ScrcpySessionKind
{
    Video,
    Audio,
    Control,
}

internal sealed record ScrcpySessionLaunchRequest(
    ResolvedAndroidDevice Device,
    ScrcpySessionKind Kind,
    Func<int, IReadOnlyList<string>> BuildServerArguments,
    OperationDeadline Deadline);

internal readonly record struct ScrcpyDeviceSessionKey(
    string DeviceKey,
    long SessionEpoch);

internal sealed class ScrcpySessionLauncher
{
    private static readonly TimeSpan _serverPushTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan _forwardTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _connectAttemptTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan _connectRetryDelay = TimeSpan.FromMilliseconds(100);
    private readonly AdbCommandRunner _commandRunner;
    private readonly IAndroidConnectionLogSink _log;
    private readonly Lazy<Task<string>> _validatedServerPath;
    private readonly ConcurrentDictionary<
        ScrcpyDeviceSessionKey,
        Lazy<Task<AdbCommandResult>>> _serverPushes = new();

    public ScrcpySessionLauncher(
        AdbCommandRunner commandRunner,
        string serverPath,
        IAndroidConnectionLogSink log)
    {
        _commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
        ArgumentException.ThrowIfNullOrWhiteSpace(serverPath);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        string fullServerPath = Path.GetFullPath(serverPath);
        _validatedServerPath = new Lazy<Task<string>>(
            () => ScrcpyServerResourceValidator.ValidateAsync(
                    fullServerPath,
                    CancellationToken.None)
                .AsTask(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async ValueTask<ScrcpyTransportLease> LaunchAsync(
        ScrcpySessionLaunchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BuildServerArguments);
        cancellationToken.ThrowIfCancellationRequested();

        string prefix = ReasonCodePrefix(request.Kind);
        string channelName = ChannelName(request.Kind);
        string validatedServer = await AwaitWithinDeadlineAsync(
                _validatedServerPath.Value,
                request.Deadline,
                $"{prefix}_SERVER_VALIDATION_TIMEOUT",
                $"校验手机{channelName}服务组件超时",
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureServerPushedAsync(
                request.Device,
                validatedServer,
                request.Deadline,
                request.Kind,
                cancellationToken)
            .ConfigureAwait(false);

        int scid = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue);
        string socketName = $"scrcpy_{scid:x8}";
        AdbCommandResult forward = await RunCommandWithinDeadlineAsync(
                [
                    "-s",
                    request.Device.Serial,
                    "forward",
                    "tcp:0",
                    $"localabstract:{socketName}",
                ],
                _forwardTimeout,
                request.Deadline,
                $"{prefix}_FORWARD_TIMEOUT",
                $"建立手机{channelName}通道超时",
                cancellationToken)
            .ConfigureAwait(false);
        EnsureCommand(
            forward,
            $"{prefix}_FORWARD",
            $"无法建立手机{channelName}通道");
        if (!int.TryParse(
                forward.StandardOutput.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int localPort) || localPort is < 1 or > 65535)
        {
            throw new AndroidConnectionException(
                $"{prefix}_FORWARD_PORT_INVALID",
                $"手机{channelName}通道返回了无效端口");
        }

        AdbManagedProcess? serverProcess = null;
        TcpClient? tcpClient = null;
        try
        {
            serverProcess = AdbManagedProcess.Start(
                _commandRunner.ExecutablePath,
                EnsurePersistentServerArguments(request.BuildServerArguments(scid)));
            tcpClient = await ConnectSocketAsync(
                    localPort,
                    serverProcess,
                    request.Device,
                    request.Kind,
                    request.Deadline,
                    cancellationToken)
                .ConfigureAwait(false);
            ScrcpyTransportLease lease = new(
                request.Device,
                request.Kind,
                localPort,
                tcpClient,
                serverProcess,
                _commandRunner,
                _log);
            tcpClient = null;
            serverProcess = null;
            return lease;
        }
        catch
        {
            tcpClient?.Dispose();
            if (serverProcess is not null)
            {
                await DisposeServerProcessAsync(
                        serverProcess,
                        request.Device,
                        request.Kind)
                    .ConfigureAwait(false);
            }

            await RemoveForwardAsync(
                    request.Device,
                    request.Kind,
                    localPort)
                .ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask<AdbCommandResult> RunDeviceCommandAsync(
        ResolvedAndroidDevice device,
        IReadOnlyList<string> deviceArguments,
        TimeSpan maximumTimeout,
        OperationDeadline deadline,
        string timeoutReasonCode,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deviceArguments);
        List<string> arguments = new(deviceArguments.Count + 2)
        {
            "-s",
            device.Serial,
        };
        arguments.AddRange(deviceArguments);
        return RunCommandWithinDeadlineAsync(
            arguments,
            maximumTimeout,
            deadline,
            timeoutReasonCode,
            timeoutMessage,
            cancellationToken);
    }

    private async ValueTask EnsureServerPushedAsync(
        ResolvedAndroidDevice device,
        string validatedServer,
        OperationDeadline deadline,
        ScrcpySessionKind kind,
        CancellationToken cancellationToken)
    {
        ScrcpyDeviceSessionKey key = new(device.DeviceKey, device.SessionEpoch);
        Lazy<Task<AdbCommandResult>> pendingPush = _serverPushes.GetOrAdd(
            key,
            _ => new Lazy<Task<AdbCommandResult>>(
                () => _commandRunner.RunAsync(
                        [
                            "-s",
                            device.Serial,
                            "push",
                            validatedServer,
                            ScrcpyProtocolConstants.DeviceServerPath,
                        ],
                        _serverPushTimeout,
                        CancellationToken.None)
                    .AsTask(),
                LazyThreadSafetyMode.ExecutionAndPublication));

        string prefix = ReasonCodePrefix(kind);
        Task<AdbCommandResult> pushTask = pendingPush.Value;
        AdbCommandResult push;
        try
        {
            push = await AwaitWithinDeadlineAsync(
                    pushTask,
                    deadline,
                    $"{prefix}_SERVER_PUSH_TIMEOUT",
                    $"把手机{ChannelName(kind)}服务发送到设备超时",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (pushTask.IsFaulted || pushTask.IsCanceled)
            {
                RemovePushIfCurrent(key, pendingPush);
            }

            throw;
        }

        if (!push.Succeeded)
        {
            RemovePushIfCurrent(key, pendingPush);
        }

        EnsureCommand(
            push,
            $"{prefix}_SERVER_PUSH",
            $"无法把{ChannelName(kind)}服务发送到手机");
        RemoveStalePushEntries(device.DeviceKey, device.SessionEpoch);
        WriteLog(
            "scrcpy_server_ready",
            AndroidReasonCodes.ScrcpyServerReady,
            "scrcpy 服务组件已在当前设备会话中就绪",
            device.DeviceKey);
    }

    private async ValueTask<AdbCommandResult> RunCommandWithinDeadlineAsync(
        IReadOnlyList<string> arguments,
        TimeSpan maximumTimeout,
        OperationDeadline deadline,
        string timeoutReasonCode,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        TimeSpan timeout = deadline.GetRemainingUpTo(maximumTimeout);
        if (timeout == TimeSpan.Zero)
        {
            throw new AndroidConnectionException(timeoutReasonCode, timeoutMessage);
        }

        return await _commandRunner.RunAsync(arguments, timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<T> AwaitWithinDeadlineAsync<T>(
        Task<T> task,
        OperationDeadline deadline,
        string timeoutReasonCode,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = deadline.Remaining;
        if (remaining == TimeSpan.Zero)
        {
            throw new AndroidConnectionException(timeoutReasonCode, timeoutMessage);
        }

        try
        {
            return await task.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new AndroidConnectionException(timeoutReasonCode, timeoutMessage);
        }
    }

    internal static IReadOnlyList<string> EnsurePersistentServerArguments(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Contains("cleanup=false", StringComparer.Ordinal))
        {
            return arguments;
        }

        List<string> persistentArguments = new(arguments.Count + 1);
        persistentArguments.AddRange(arguments);
        persistentArguments.Add("cleanup=false");
        return persistentArguments;
    }

    private async ValueTask<TcpClient> ConnectSocketAsync(
        int localPort,
        AdbManagedProcess serverProcess,
        ResolvedAndroidDevice device,
        ScrcpySessionKind kind,
        OperationDeadline deadline,
        CancellationToken cancellationToken)
    {
        string prefix = ReasonCodePrefix(kind);
        string channelName = ChannelName(kind);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsExpired)
            {
                throw new AndroidConnectionException(
                    $"{prefix}_CONNECT_TIMEOUT",
                    $"等待手机{channelName}服务连接超时");
            }

            if (serverProcess.HasExited)
            {
                string outputTail = SanitizeServerOutput(
                    serverProcess.GetOutputTail(),
                    device.Serial);
                if (!string.IsNullOrWhiteSpace(outputTail))
                {
                    WriteLog(
                        "scrcpy_server_exit_diagnostic",
                        $"{prefix}_SERVER_EXIT_DIAGNOSTIC",
                        $"手机{channelName}服务启动输出：{outputTail}",
                        device.DeviceKey);
                }

                throw new AndroidConnectionException(
                    $"{prefix}_SERVER_EXITED",
                    $"手机{channelName}服务在建立连接前退出");
            }

            TcpClient? client = new(AddressFamily.InterNetwork) { NoDelay = true };
            try
            {
                TimeSpan attemptTimeout = deadline.GetRemainingUpTo(_connectAttemptTimeout);
                if (attemptTimeout == TimeSpan.Zero)
                {
                    throw new AndroidConnectionException(
                        $"{prefix}_CONNECT_TIMEOUT",
                        $"等待手机{channelName}服务连接超时");
                }

                using CancellationTokenSource attemptSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptSource.CancelAfter(attemptTimeout);
                await client.ConnectAsync(
                        IPAddress.Loopback,
                        localPort,
                        attemptSource.Token)
                    .ConfigureAwait(false);
                byte[] dummy = new byte[1];
                await client.GetStream()
                    .ReadExactlyAsync(dummy, attemptSource.Token)
                    .ConfigureAwait(false);
                if (dummy[0] != 0)
                {
                    throw new IOException("Invalid scrcpy forward tunnel byte.");
                }

                TcpClient connected = client;
                client = null;
                return connected;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is SocketException or IOException or OperationCanceledException)
            {
                if (deadline.IsExpired)
                {
                    throw new AndroidConnectionException(
                        $"{prefix}_CONNECT_TIMEOUT",
                        $"等待手机{channelName}服务连接超时");
                }
            }
            finally
            {
                client?.Dispose();
            }

            TimeSpan retryDelay = deadline.GetRemainingUpTo(_connectRetryDelay);
            if (retryDelay == TimeSpan.Zero)
            {
                throw new AndroidConnectionException(
                    $"{prefix}_CONNECT_TIMEOUT",
                    $"等待手机{channelName}服务连接超时");
            }

            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static string SanitizeServerOutput(string output, string serial)
    {
        const int maximumLength = 800;
        string sanitized = output.Replace(serial, "<device>", StringComparison.Ordinal);
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[^maximumLength..];
    }

    private async ValueTask RemoveForwardAsync(
        ResolvedAndroidDevice device,
        ScrcpySessionKind kind,
        int localPort)
    {
        try
        {
            AdbCommandResult result = await _commandRunner.RunAsync(
                    [
                        "-s",
                        device.Serial,
                        "forward",
                        "--remove",
                        $"tcp:{localPort}",
                    ],
                    TimeSpan.FromSeconds(3),
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                WriteLog(
                    "scrcpy_forward_cleanup_failed",
                    $"{ReasonCodePrefix(kind)}_FORWARD_CLEANUP_FAILED",
                    $"手机{ChannelName(kind)}端口转发清理失败",
                    device.DeviceKey);
            }
        }
        catch (Exception exception) when (IsExpectedCleanupFailure(exception))
        {
            WriteLog(
                "scrcpy_forward_cleanup_failed",
                $"{ReasonCodePrefix(kind)}_FORWARD_CLEANUP_FAILED",
                $"手机{ChannelName(kind)}端口转发清理失败",
                device.DeviceKey);
        }
    }

    private async ValueTask DisposeServerProcessAsync(
        AdbManagedProcess serverProcess,
        ResolvedAndroidDevice device,
        ScrcpySessionKind kind)
    {
        try
        {
            await serverProcess.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedCleanupFailure(exception))
        {
            WriteLog(
                "scrcpy_server_cleanup_failed",
                $"{ReasonCodePrefix(kind)}_SERVER_CLEANUP_FAILED",
                $"手机{ChannelName(kind)}服务进程停止失败",
                device.DeviceKey);
        }

        try
        {
            await serverProcess.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (IsExpectedCleanupFailure(exception))
        {
            WriteLog(
                "scrcpy_server_cleanup_failed",
                $"{ReasonCodePrefix(kind)}_SERVER_CLEANUP_FAILED",
                $"手机{ChannelName(kind)}服务进程释放失败",
                device.DeviceKey);
        }
    }

    private void RemovePushIfCurrent(
        ScrcpyDeviceSessionKey key,
        Lazy<Task<AdbCommandResult>> expected)
    {
        if (_serverPushes.TryGetValue(key, out Lazy<Task<AdbCommandResult>>? current) &&
            ReferenceEquals(current, expected))
        {
            _serverPushes.TryRemove(key, out _);
        }
    }

    private static bool IsExpectedCleanupFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or UnauthorizedAccessException or
            Win32Exception;

    private void RemoveStalePushEntries(string deviceKey, long currentEpoch)
    {
        foreach (ScrcpyDeviceSessionKey key in _serverPushes.Keys)
        {
            if (string.Equals(key.DeviceKey, deviceKey, StringComparison.Ordinal) &&
                key.SessionEpoch != currentEpoch)
            {
                _serverPushes.TryRemove(key, out _);
            }
        }
    }

    private static void EnsureCommand(
        AdbCommandResult result,
        string operation,
        string message)
    {
        if (result.TimedOut)
        {
            throw new AndroidConnectionException($"{operation}_TIMEOUT", $"{message}：操作超时");
        }

        if (!result.Succeeded)
        {
            throw new AndroidConnectionException($"{operation}_FAILED", message);
        }
    }

    internal static string ReasonCodePrefix(ScrcpySessionKind kind) => kind switch
    {
        ScrcpySessionKind.Video => AndroidReasonCodes.Video,
        ScrcpySessionKind.Audio => AndroidReasonCodes.Audio,
        ScrcpySessionKind.Control => AndroidReasonCodes.Control,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static string ChannelName(ScrcpySessionKind kind) => kind switch
    {
        ScrcpySessionKind.Video => "视频",
        ScrcpySessionKind.Audio => "音频",
        ScrcpySessionKind.Control => "控制",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private void WriteLog(
        string eventName,
        string reasonCode,
        string message,
        string deviceKey)
    {
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            eventName,
            reasonCode,
            message,
            deviceKey));
    }
}
