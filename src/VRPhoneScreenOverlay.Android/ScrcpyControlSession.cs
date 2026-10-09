using System.Net.Sockets;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyControlSession(
    string deviceKey,
    ScrcpyTransportLease transport,
    ScrcpyControlProtocolWriter protocol,
    AndroidDisplaySize displaySize,
    IAndroidConnectionLogSink log) : IAndroidControlSession
{
    private readonly ScrcpyTransportLease _transport = transport;
    private readonly ScrcpyControlProtocolWriter _protocol = protocol;
    private readonly AndroidDisplaySize _displaySize = displaySize;
    private readonly IAndroidConnectionLogSink _log = log;
    private long _commandCount;
    private long _moveCount;
    private bool _disposed;
    private bool _displayPowerOff;

    public string DeviceKey { get; } = deviceKey;

    public async ValueTask SendAsync(
        PhoneInputCommand command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_transport.HasServerExited)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.ControlServerExited,
                "手机控制服务已经停止");
        }

        try
        {
            PhoneInputCommand mapped = _displaySize.Map(command);
            await _protocol.WriteAsync(mapped, cancellationToken).ConfigureAwait(false);
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
            throw new AndroidConnectionException(
                AndroidReasonCodes.ControlWriteFailed,
                "手机控制指令发送失败");
        }

        _commandCount++;
        // Periodic guard/activity traffic is counted, not logged ten times a second.
        if (command.Kind is PhoneInputCommandKind.MaintainDisplayOff or
            PhoneInputCommandKind.WakeScreenWhileDisplayOff or PhoneInputCommandKind.UserActivity) { return; }
        if (command.Kind == PhoneInputCommandKind.PointerMove)
        {
            _moveCount++;
            return;
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
                    DurationMilliseconds: (long)result.Duration.TotalMilliseconds));
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException)
        {
            _log.TryWrite(new AndroidConnectionLogEntry(
                DateTimeOffset.UtcNow,
                "control_wake_fallback",
                AndroidReasonCodes.WakeKeyFailed,
                "scrcpy 唤醒指令已发送，但 Android 唤醒键发送失败",
                DeviceKey));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_displayPowerOff && !_transport.HasServerExited)
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
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            "control_stopped",
            AndroidReasonCodes.ControlStopped,
            $"手机控制通道已停止；指令 {_commandCount}；合并后 MOVE {_moveCount}",
            DeviceKey,
            PacketCount: _commandCount));
    }
}
