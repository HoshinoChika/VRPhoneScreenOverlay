using System.Net.Sockets;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyControlSession(
    string deviceKey,
    ScrcpyTransportLease transport,
    ScrcpyControlProtocolWriter protocol,
    AndroidDisplaySize displaySize,
    IAndroidConnectionLogSink log, IPhoneClipboardSink? clipboard = null) : IAndroidControlSession
{
    private readonly ScrcpyClipboardReceiver? _clipboard = clipboard is null ? null : new(transport.Stream, clipboard,
        ended => log.TryWrite(new AndroidConnectionLogEntry(ended.At, "phone_clipboard_state", ended.ReasonCode,
            "手机剪贴板接收结束", deviceKey, PacketCount: ended.FailureCount)));
    private readonly ScrcpyTransportLease _transport = transport;
    private readonly ScrcpyControlProtocolWriter _protocol = protocol;
    private ControlGeometryWriter? _geometry;
    private readonly IAndroidConnectionLogSink _log = log;
    private long _commandCount;
    private long _moveCount;
    private bool _disposed;
    private bool _displayPowerOff;
    private int _aborted;

    public void AbortPendingOperations()
    {
        if (Interlocked.Exchange(ref _aborted, 1) == 0) { _clipboard?.RequestStop(); _transport.AbortStream(); }
    }

    public string DeviceKey { get; } = deviceKey;
    public ServerOutputDiagnostic ServerOutput => _transport.ServerOutput;
    public bool SupportsGeometryRefresh => true;
    public ControlGeometryDiagnostic? Geometry => Volatile.Read(ref _geometry)?.Snapshot;

    public async ValueTask SendAsync(
        PhoneInputCommand command,
        CancellationToken cancellationToken)
        => _ = await SendWithResultAsync(command, cancellationToken).ConfigureAwait(false);

    public async ValueTask<PhoneInputWriteResult> SendWithResultAsync(
        PhoneInputCommand command, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Volatile.Read(ref _aborted) != 0)
        { throw new AndroidConnectionException(AndroidReasonCodes.ControlChannelAborted, "手机控制通道已中止，请重新连接"); }
        if (_transport.HasServerExited)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.ControlServerExited,
                "手机控制服务已经停止");
        }

        PhoneInputWriteResult delivery;
        try
        {
            if (_geometry is null) { Volatile.Write(ref _geometry, new(displaySize, ReadDisplaySizeAsync, _protocol.WriteAsync)); }
            delivery = await _geometry.SendAsync(command, cancellationToken).ConfigureAwait(false);
            if (!delivery.Written) { return delivery; }
            if (command.Kind is PhoneInputCommandKind.TurnDisplayOff or PhoneInputCommandKind.MaintainDisplayOff or
                PhoneInputCommandKind.WakeScreenWhileDisplayOff or PhoneInputCommandKind.RestoreDisplayPower or PhoneInputCommandKind.WakeScreen)
            {
                _displayPowerOff = command.Kind is PhoneInputCommandKind.TurnDisplayOff or
                    PhoneInputCommandKind.MaintainDisplayOff or PhoneInputCommandKind.WakeScreenWhileDisplayOff;
            }
            if (command.Kind == PhoneInputCommandKind.WakeScreen)
            {
                await SendAndroidWakeKeyAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException)
        {
            AbortPendingOperations();
            throw new AndroidConnectionException(
                AndroidReasonCodes.ControlWriteFailed,
                "手机控制指令发送失败", exception);
        }
        catch (OperationCanceledException) { AbortPendingOperations(); throw; }

        _commandCount++;
        // Periodic guard/activity traffic is counted, not logged ten times a second.
        if (command.Kind is PhoneInputCommandKind.MaintainDisplayOff or
                PhoneInputCommandKind.WakeScreenWhileDisplayOff or PhoneInputCommandKind.UserActivity) { return delivery; }
        if (command.Kind == PhoneInputCommandKind.PointerMove)
        {
            _moveCount++;
            return delivery;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        _log.TryWrite(new AndroidConnectionLogEntry(
            now,
            "control_command",
            AndroidReasonCodes.ControlCommandSent,
            $"手机控制指令已发送：{command.Kind}",
            DeviceKey,
            DurationMilliseconds: Math.Max(
                0,
                (long)(now - command.CreatedAt).TotalMilliseconds),
            CommandSequence: command.Sequence,
            CommandKind: command.Kind.ToString()));
        return delivery;
    }

    private async ValueTask<AndroidDisplaySize> ReadDisplaySizeAsync(CancellationToken token)
    {
        AdbCommandResult result = await _transport.RunDeviceCommandAsync(["shell", "wm", "size"], TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        if (!result.Succeeded || !AndroidDisplaySize.TryParseWmSize(result.StandardOutput, out AndroidDisplaySize size))
        {
            _log.TryWrite(new(DateTimeOffset.UtcNow, "control_geometry", "ANDROID_CONTROL_DISPLAY_REFRESH_FAILED",
                "手机显示尺寸未能重新确认，已停止本通道触控", DeviceKey)
            { AdbCommand = result.Diagnostic });
            throw new AndroidConnectionException(result.TimedOut ? "ANDROID_CONTROL_DISPLAY_REFRESH_TIMEOUT" :
                "ANDROID_CONTROL_DISPLAY_REFRESH_FAILED", "手机显示尺寸暂未确认，坐标输入已暂停")
            { CommandDiagnostic = result.Diagnostic };
        }
        AdbCommandResult rotation = await _transport.RunDeviceCommandAsync(["shell", "dumpsys", "-t", "1", "display"], TimeSpan.FromMilliseconds(500), token).ConfigureAwait(false);
        (int? value, string status) = rotation.Succeeded ? AndroidDisplayRotation.Parse(rotation.StandardOutput) : (null, "command-failed");
        return size with { Rotation = value, RotationStatus = status };
    }

    private async ValueTask SendAndroidWakeKeyAsync(CancellationToken cancellationToken)
    {
        try
        {
            AdbCommandResult result = await _transport.RunDeviceCommandAsync(
                    ["shell", "input", "keyevent", "224"],
                    TimeSpan.FromSeconds(2),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _log.TryWrite(new AndroidConnectionLogEntry(
                    DateTimeOffset.UtcNow,
                    "control_wake_fallback",
                    result.TimedOut
                        ? AndroidReasonCodes.WakeKeyTimeout
                        : AndroidReasonCodes.WakeKeyFailed,
                    "scrcpy 唤醒指令已发送，但 Android 唤醒键发送失败",
                    DeviceKey,
                    DurationMilliseconds: (long)result.Duration.TotalMilliseconds)
                { AdbCommand = result.Diagnostic });
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or AndroidConnectionException or System.ComponentModel.Win32Exception)
        {
            _log.TryWrite(new AndroidConnectionLogEntry(
                DateTimeOffset.UtcNow,
                "control_wake_fallback",
                AndroidReasonCodes.WakeKeyFailed,
                "scrcpy 唤醒指令已发送，但 Android 唤醒键发送失败",
                DeviceKey)
            {
                AdbCommand = AdbDiagnosticPolicy.Describe(["shell", "input", "keyevent"], "wake-fallback") with
                { ExceptionType = exception.GetType().FullName, HResult = exception.HResult, ErrorCategory = "wake-fallback-failed" }
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clipboard?.RequestStop();
        if (_displayPowerOff && Volatile.Read(ref _aborted) == 0 && !_transport.HasServerExited)
        {
            using CancellationTokenSource restore = new(TimeSpan.FromSeconds(2));
            try
            {
                await _protocol.WriteAsync(new PhoneInputCommand(0,
                    PhoneInputCommandKind.RestoreDisplayPower, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow),
                    restore.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException)
            {
                // A lost USB connection cannot restore physical display power. Transport
                // teardown must still complete; the phone's power key remains available.
            }
        }
        await _transport.DisposeAsync().ConfigureAwait(false);
        if (_clipboard is not null) { await _clipboard.DisposeAsync().ConfigureAwait(false); }
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            "control_stopped",
            AndroidReasonCodes.ControlStopped,
            $"手机控制通道已停止；指令 {_commandCount}；合并后 MOVE {_moveCount}",
            DeviceKey,
            PacketCount: _commandCount));
    }
}
