using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyControlSessionFactory(
    AndroidConnectionService connection,
    ScrcpySessionLauncher launcher,
    IAndroidConnectionLogSink log) : IAndroidControlSessionFactory
{
    private static readonly TimeSpan _startupTimeout = TimeSpan.FromSeconds(15);
    private readonly AndroidConnectionService _connection = connection;
    private readonly ScrcpySessionLauncher _launcher = launcher;
    private readonly IAndroidConnectionLogSink _log = log;

    public async ValueTask<IAndroidControlSession> OpenAsync(
        string? deviceKey,
        CancellationToken cancellationToken)
    {
        if (!_connection.TryResolveReadyDevice(deviceKey, out ResolvedAndroidDevice device))
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.ControlDeviceNotReady,
                "没有已授权且可用的安卓手机");
        }

        OperationDeadline deadline = OperationDeadline.Start(_startupTimeout);
        AndroidDisplaySize displaySize = await ReadDisplaySizeAsync(
                device,
                deadline,
                cancellationToken)
            .ConfigureAwait(false);
        ScrcpyTransportLease transport = await _launcher.LaunchAsync(
                new ScrcpySessionLaunchRequest(
                    device,
                    ScrcpySessionKind.Control,
                    scid => BuildServerArguments(device.Serial, scid),
                    deadline),
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ScrcpyControlProtocolWriter protocol = new(transport.Stream);
            _log.TryWrite(new AndroidConnectionLogEntry(
                DateTimeOffset.UtcNow,
                "control_connected",
                AndroidReasonCodes.ControlConnected,
                "独立手机控制通道已连接",
                device.DeviceKey));
            return new ScrcpyControlSession(
                device.DeviceKey,
                transport,
                protocol,
                displaySize,
                _log);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<AndroidDisplaySize> ReadDisplaySizeAsync(
        ResolvedAndroidDevice device,
        OperationDeadline deadline,
        CancellationToken cancellationToken)
    {
        AdbCommandResult result = await _launcher.RunDeviceCommandAsync(
                device,
                ["shell", "wm", "size"],
                TimeSpan.FromSeconds(3),
                deadline,
                AndroidReasonCodes.ControlDisplaySizeTimeout,
                "读取手机原生显示尺寸超时",
                cancellationToken)
            .ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.ControlDisplaySizeTimeout,
                "读取手机原生显示尺寸超时，未启动触控以避免坐标偏移");
        }

        if (!result.Succeeded ||
            !AndroidDisplaySize.TryParseWmSize(result.StandardOutput, out AndroidDisplaySize size))
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.ControlDisplaySizeFailed,
                "无法读取手机原生显示尺寸，未启动触控以避免坐标偏移");
        }

        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            "control_display_size",
            AndroidReasonCodes.ControlDisplaySizeReady,
            $"手机触控使用原生显示尺寸 {size.Width}x{size.Height}",
            device.DeviceKey,
            VideoWidth: size.Width,
            VideoHeight: size.Height));
        return size;
    }

    private static string[] BuildServerArguments(string serial, int scid) =>
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
        "video=false",
        "audio=false",
        "control=true",
        "tunnel_forward=true",
        "clipboard_autosync=false",
        "send_device_meta=false",
        "send_frame_meta=false",
        "send_stream_meta=false",
    ];
}
