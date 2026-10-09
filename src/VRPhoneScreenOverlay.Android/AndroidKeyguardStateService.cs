namespace VRPhoneScreenOverlay.Android;

public enum AndroidKeyguardState
{
    Unknown,
    Unlocked,
    Locked,
}

public sealed record AndroidKeyguardSnapshot(
    AndroidKeyguardState State,
    string ReasonCode,
    string Message);

public interface IAndroidKeyguardStateService
{
    public ValueTask<AndroidKeyguardSnapshot> ProbeAsync(
        string? deviceKey,
        CancellationToken cancellationToken);
}

internal sealed class AndroidKeyguardStateService(
    AndroidConnectionService connection,
    AdbCommandRunner commandRunner) : IAndroidKeyguardStateService
{
    private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(2);
    private readonly AndroidConnectionService _connection = connection;
    private readonly AdbCommandRunner _commandRunner = commandRunner;

    public async ValueTask<AndroidKeyguardSnapshot> ProbeAsync(
        string? deviceKey,
        CancellationToken cancellationToken)
    {
        if (!_connection.TryResolveReadyDevice(deviceKey, out ResolvedAndroidDevice device))
        {
            return new AndroidKeyguardSnapshot(
                AndroidKeyguardState.Unknown,
                AndroidReasonCodes.KeyguardDeviceNotReady,
                "手机尚未连接，无法检测锁屏状态");
        }

        AdbCommandResult result = await _commandRunner.RunAsync(
                ["-s", device.Serial, "shell", "dumpsys", "window", "policy"],
                _probeTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return new AndroidKeyguardSnapshot(
                AndroidKeyguardState.Unknown,
                result.TimedOut
                    ? AndroidReasonCodes.KeyguardProbeTimeout
                    : AndroidReasonCodes.KeyguardProbeFailed,
                "暂时无法读取手机锁屏状态");
        }

        AndroidKeyguardState state = AndroidKeyguardOutputParser.Parse(result.StandardOutput);
        return state switch
        {
            AndroidKeyguardState.Locked => new AndroidKeyguardSnapshot(
                state,
                AndroidReasonCodes.KeyguardLocked,
                "检测到手机处于密码锁屏状态"),
            AndroidKeyguardState.Unlocked => new AndroidKeyguardSnapshot(
                state,
                AndroidReasonCodes.KeyguardUnlocked,
                "手机未处于密码锁屏状态"),
            _ => new AndroidKeyguardSnapshot(
                state,
                AndroidReasonCodes.KeyguardUnknown,
                "当前手机无法可靠识别密码锁屏界面"),
        };
    }
}

internal static class AndroidKeyguardOutputParser
{
    private static readonly string[] _lockedMarkers =
    [
        "showing=true",
        "showingandnotoccluded=true",
        "mshowinglockscreen=true",
        "isstatusbarkeyguard=true",
        "keyguardshowing=true",
        "devicelocked=true",
    ];

    private static readonly string[] _unlockedMarkers =
    [
        "showing=false",
        "showingandnotoccluded=false",
        "mshowinglockscreen=false",
        "isstatusbarkeyguard=false",
        "keyguardshowing=false",
        "devicelocked=false",
    ];

    public static AndroidKeyguardState Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        bool unlockedMarkerFound = false;
        foreach (string rawLine in output.Split(['\r', '\n']))
        {
            string line = rawLine
                .Trim()
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
            if (!IsKeyguardLine(line))
            {
                continue;
            }

            if (_lockedMarkers.Any(marker => line.Contains(marker, StringComparison.Ordinal)))
            {
                return AndroidKeyguardState.Locked;
            }

            unlockedMarkerFound |= _unlockedMarkers.Any(
                marker => line.Contains(marker, StringComparison.Ordinal));
        }

        return unlockedMarkerFound
            ? AndroidKeyguardState.Unlocked
            : AndroidKeyguardState.Unknown;
    }

    private static bool IsKeyguardLine(string line) =>
        line.Contains("keyguard", StringComparison.Ordinal) ||
        line.Contains("lockscreen", StringComparison.Ordinal) ||
        line.StartsWith("showing=", StringComparison.Ordinal) ||
        line.StartsWith("showingandnotoccluded=", StringComparison.Ordinal) ||
        line.StartsWith("devicelocked=", StringComparison.Ordinal);
}
