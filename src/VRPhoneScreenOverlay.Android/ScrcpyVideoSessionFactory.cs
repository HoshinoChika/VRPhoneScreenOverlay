using System.Diagnostics;
using System.Globalization;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyVideoSessionFactory(
    AndroidConnectionService connection,
    ScrcpySessionLauncher launcher,
    IAndroidConnectionLogSink log) : IAndroidVideoSessionFactory
{
    private static readonly TimeSpan _startupTimeout = TimeSpan.FromSeconds(15);
    private readonly AndroidConnectionService _connection = connection;
    private readonly ScrcpySessionLauncher _launcher = launcher;
    private readonly IAndroidConnectionLogSink _log = log;

    public async ValueTask<IAndroidVideoSession> OpenAsync(
        string? deviceKey,
        AndroidVideoOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!_connection.TryResolveReadyDevice(deviceKey, out ResolvedAndroidDevice device))
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.VideoDeviceNotReady,
                "没有已授权且可用的安卓手机");
        }

        OperationDeadline deadline = OperationDeadline.Start(_startupTimeout);
        if (options.PowerOnDevice)
        {
            await WakeDeviceBeforeVideoAsync(device, deadline, cancellationToken)
                .ConfigureAwait(false);
        }

        AndroidVideoClockSynchronizer? videoClock = await TrySynchronizeVideoClockAsync(
                device,
                deadline,
                cancellationToken)
            .ConfigureAwait(false);

        ScrcpyTransportLease transport = await _launcher.LaunchAsync(
                new ScrcpySessionLaunchRequest(
                    device,
                    ScrcpySessionKind.Video,
                    scid => BuildServerArguments(device.Serial, scid, options),
                    deadline),
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ScrcpyVideoProtocolReader protocol = new(transport.Stream);
            ScrcpyVideoHandshake handshake = await ReadHandshakeAsync(
                    protocol,
                    deadline,
                    cancellationToken)
                .ConfigureAwait(false);
            WriteLog(
                "video_connected",
                AndroidReasonCodes.VideoConnected,
                $"手机编码视频流已连接，编码 {handshake.Codec}",
                device.DeviceKey);
            return new ScrcpyVideoSession(
                device.DeviceKey,
                handshake.DeviceName,
                handshake.Codec,
                transport,
                protocol,
                videoClock,
                _log);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<AndroidVideoClockSynchronizer?> TrySynchronizeVideoClockAsync(
        ResolvedAndroidDevice device,
        OperationDeadline deadline,
        CancellationToken cancellationToken)
    {
        string marker = $"vps-{Guid.NewGuid():N}";
        long startedTimestamp = Stopwatch.GetTimestamp();
        try
        {
            AdbCommandResult markerResult = await _launcher.RunDeviceCommandAsync(
                    device,
                    ["shell", "log", "-t", "VRPhoneScreenClock", marker],
                    TimeSpan.FromSeconds(1),
                    deadline,
                    AndroidReasonCodes.VideoClockSyncTimeout,
                    "同步手机视频时钟超时",
                    cancellationToken)
                .ConfigureAwait(false);
            long completedTimestamp = Stopwatch.GetTimestamp();
            if (!markerResult.Succeeded)
            {
                return LogVideoClockUnavailable(device.DeviceKey);
            }

            AdbCommandResult logcat = await _launcher.RunDeviceCommandAsync(
                    device,
                    [
                        "shell",
                        "logcat",
                        "-v",
                        "threadtime",
                        "-v",
                        "monotonic",
                        "-v",
                        "usec",
                        "-d",
                        "-t",
                        "20",
                        "-s",
                        "VRPhoneScreenClock:I",
                        "*:S",
                    ],
                    TimeSpan.FromSeconds(1),
                    deadline,
                    AndroidReasonCodes.VideoClockSyncTimeout,
                    "同步手机视频时钟超时",
                    cancellationToken)
                .ConfigureAwait(false);
            if (logcat.Succeeded && TryReadMonotonicSeconds(
                    logcat.StandardOutput,
                    marker,
                    out string monotonicSeconds) &&
                AndroidVideoClockSynchronizer.TryCreate(
                    monotonicSeconds,
                    startedTimestamp,
                    completedTimestamp,
                    out AndroidVideoClockSynchronizer? synchronizer))
            {
                WriteLog(
                    "video_clock_synchronized",
                    AndroidReasonCodes.VideoClockSynchronized,
                    $"手机视频时钟已同步，往返 {synchronizer!.RoundTripDuration.TotalMilliseconds:F0} ms",
                    device.DeviceKey);
                return synchronizer;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AndroidConnectionException)
        {
        }

        return LogVideoClockUnavailable(device.DeviceKey);
    }

    private AndroidVideoClockSynchronizer? LogVideoClockUnavailable(string deviceKey)
    {
        WriteLog(
            "video_clock_unavailable",
            AndroidReasonCodes.VideoClockUnavailable,
            "手机视频时钟暂不可同步，延迟数据不可用",
            deviceKey);
        return null;
    }

    private static bool TryReadMonotonicSeconds(
        string logcatOutput,
        string marker,
        out string monotonicSeconds)
    {
        foreach (string line in logcatOutput.Split(['\r', '\n']))
        {
            if (!line.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string part in line.Split(
                [' ', '\t', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (double.TryParse(
                    part,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out double value) && value > 0)
                {
                    monotonicSeconds = part;
                    return true;
                }
            }
        }

        monotonicSeconds = string.Empty;
        return false;
    }

    private async ValueTask WakeDeviceBeforeVideoAsync(
        ResolvedAndroidDevice device,
        OperationDeadline deadline,
        CancellationToken cancellationToken)
    {
        AdbCommandResult wake = await _launcher.RunDeviceCommandAsync(
                device,
                ["shell", "input", "keyevent", "224"],
                TimeSpan.FromSeconds(2),
                deadline,
                AndroidReasonCodes.VideoWakeTimeout,
                "启动视频前唤醒手机超时",
                cancellationToken)
            .ConfigureAwait(false);
        if (!wake.Succeeded)
        {
            WriteLog(
                "video_wake_failed",
                wake.TimedOut ? AndroidReasonCodes.VideoWakeTimeout : AndroidReasonCodes.VideoWakeFailed,
                "启动视频前唤醒手机失败，将继续尝试建立视频链路",
                device.DeviceKey);
            return;
        }

        WriteLog(
            "video_wake_sent",
            AndroidReasonCodes.VideoWakeSent,
            "启动视频前已发送手机唤醒指令",
            device.DeviceKey);
        TimeSpan wakeSettleDelay = deadline.GetRemainingUpTo(TimeSpan.FromMilliseconds(300));
        if (wakeSettleDelay > TimeSpan.Zero)
        {
            await Task.Delay(wakeSettleDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask<ScrcpyVideoHandshake> ReadHandshakeAsync(
        ScrcpyVideoProtocolReader protocol,
        OperationDeadline deadline,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadlineSource =
            deadline.CreateCancellationSource(cancellationToken);
        try
        {
            return await protocol.ReadHandshakeAsync(deadlineSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.VideoHandshakeTimeout,
                "等待手机视频协议握手超时");
        }
    }

    private static string[] BuildServerArguments(
        string serial,
        int scid,
        AndroidVideoOptions options)
    {
        List<string> arguments =
        [
            "-s",
            serial,
            "shell",
            $"CLASSPATH={ScrcpyProtocolConstants.DeviceServerPath}",
            "app_process",
            "/",
            "com.genymobile.scrcpy.Server",
            ScrcpyProtocolConstants.ServerVersion,
            $"scid={scid:x8}",
            "log_level=info",
            "video_codec=h264",
            $"video_bit_rate={options.VideoBitrateBitsPerSecond.ToString(CultureInfo.InvariantCulture)}",
            "audio=false",
            $"max_fps={options.MaximumFramesPerSecond.ToString(CultureInfo.InvariantCulture)}",
            "tunnel_forward=true",
            "control=false",
            "clipboard_autosync=false",
        ];
        if (options.MaximumSize > 0)
        {
            arguments.Add($"max_size={options.MaximumSize.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!options.PowerOnDevice)
        {
            arguments.Add("power_on=false");
        }

        return [.. arguments];
    }

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
