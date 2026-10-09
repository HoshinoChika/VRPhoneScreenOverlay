namespace VRPhoneScreenOverlay.Android;

public sealed record AndroidMediaPauseLease(
    string? DeviceKey,
    bool ResumeRequired,
    string ReasonCode,
    string Message);

public interface IAndroidMediaPlaybackControl
{
    public ValueTask<AndroidMediaPauseLease> PauseForVrInterruptionAsync(
        string? deviceKey,
        CancellationToken cancellationToken);

    public ValueTask ResumeAfterVrReadyAsync(
        AndroidMediaPauseLease lease,
        CancellationToken cancellationToken);
}

internal sealed class AndroidMediaPlaybackControl(
    AndroidConnectionService connection,
    AdbCommandRunner commandRunner,
    IAndroidConnectionLogSink log) : IAndroidMediaPlaybackControl
{
    private readonly AndroidConnectionService _connection = connection;
    private readonly AdbCommandRunner _commandRunner = commandRunner;
    private readonly IAndroidConnectionLogSink _log = log;

    public async ValueTask<AndroidMediaPauseLease> PauseForVrInterruptionAsync(
        string? deviceKey,
        CancellationToken cancellationToken)
    {
        if (!_connection.TryResolveReadyDevice(deviceKey, out ResolvedAndroidDevice device))
        {
            return new AndroidMediaPauseLease(
                deviceKey,
                false,
                AndroidReasonCodes.MediaDeviceNotReady,
                "没有可暂停媒体播放的手机");
        }

        AdbCommandResult state = await _commandRunner.RunAsync(
            ["-s", device.Serial, "shell", "dumpsys", "media_session"],
            TimeSpan.FromSeconds(3),
            cancellationToken).ConfigureAwait(false);
        if (!state.Succeeded || !IsPlaybackActive(state.StandardOutput))
        {
            string reasonCode = state.Succeeded
                ? AndroidReasonCodes.MediaAlreadyPaused
                : AndroidReasonCodes.MediaStateUnavailable;
            string message = state.Succeeded
                ? "手机媒体当前未播放，无需暂停"
                : "无法确认手机媒体播放状态，将继续重启且不会误启动媒体";
            WriteLog(reasonCode, message, device.DeviceKey);
            return new AndroidMediaPauseLease(device.DeviceKey, false, reasonCode, message);
        }

        AdbCommandResult pause = await SendMediaKeyAsync(
            device,
            127,
            cancellationToken).ConfigureAwait(false);
        if (!pause.Succeeded)
        {
            throw new AndroidConnectionException(
                pause.TimedOut ? AndroidReasonCodes.MediaPauseTimeout : AndroidReasonCodes.MediaPauseFailed,
                "关闭或重启 VR 手机画面前无法暂停手机媒体播放");
        }

        // Match the proven Lite shutdown ordering: Android needs a brief chance to consume
        // KEYCODE_MEDIA_PAUSE before scrcpy releases its video/audio route.
        await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);

        const string pausedCode = AndroidReasonCodes.MediaPausedForVrInterruption;
        const string pausedMessage = "手机媒体已暂停，等待 VR 画面恢复";
        WriteLog(pausedCode, pausedMessage, device.DeviceKey);
        return new AndroidMediaPauseLease(device.DeviceKey, true, pausedCode, pausedMessage);
    }

    public async ValueTask ResumeAfterVrReadyAsync(
        AndroidMediaPauseLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (!lease.ResumeRequired)
        {
            return;
        }

        if (!_connection.TryResolveReadyDevice(lease.DeviceKey, out ResolvedAndroidDevice device))
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.MediaResumeDeviceNotReady,
                "VR 画面已恢复，但手机连接不可用，无法继续原来的媒体播放");
        }

        AdbCommandResult resume = await SendMediaKeyAsync(
            device,
            126,
            cancellationToken).ConfigureAwait(false);
        if (!resume.Succeeded)
        {
            throw new AndroidConnectionException(
                resume.TimedOut ? AndroidReasonCodes.MediaResumeTimeout : AndroidReasonCodes.MediaResumeFailed,
                "VR 画面已恢复，但手机媒体继续播放失败");
        }

        WriteLog(
            AndroidReasonCodes.MediaResumedAfterVrReady,
            "VR 画面恢复后已继续手机媒体播放",
            device.DeviceKey);
    }

    internal static bool IsPlaybackActive(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        bool inSessionsStack = false;
        bool activeSession = false;
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("Sessions Stack", StringComparison.OrdinalIgnoreCase))
            {
                inSessionsStack = true;
                activeSession = false;
                continue;
            }

            if (!inSessionsStack)
            {
                continue;
            }

            if (line.StartsWith("active=", StringComparison.OrdinalIgnoreCase))
            {
                activeSession = line.Contains("active=true", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (activeSession &&
                IsPlayingStateLine(line))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlayingStateLine(string line)
    {
        const string prefix = "state=PlaybackState";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> fields = line.AsSpan(prefix.Length).TrimStart();
        if (fields.IsEmpty || fields[0] != '{')
        {
            return false;
        }

        fields = fields[1..].TrimStart();
        const string statePrefix = "state=";
        if (!fields.StartsWith(statePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> state = fields[statePrefix.Length..];
        int end = state.IndexOfAny(',', '}');
        if (end < 0)
        {
            return false;
        }

        state = state[..end].Trim();
        // Android versions differ in toString formatting. Match the complete
        // state field, so an error message or state 30 cannot impersonate 3.
        return state.Equals("3", StringComparison.Ordinal) ||
            state.Equals("PLAYING(3)", StringComparison.OrdinalIgnoreCase);
    }

    private ValueTask<AdbCommandResult> SendMediaKeyAsync(
        ResolvedAndroidDevice device,
        int keyCode,
        CancellationToken cancellationToken) => _commandRunner.RunAsync(
        ["-s", device.Serial, "shell", "input", "keyevent", keyCode.ToString(System.Globalization.CultureInfo.InvariantCulture)],
        TimeSpan.FromSeconds(3),
        cancellationToken);

    private void WriteLog(string reasonCode, string message, string deviceKey)
    {
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            "android_media_playback",
            reasonCode,
            message,
            deviceKey));
    }
}
