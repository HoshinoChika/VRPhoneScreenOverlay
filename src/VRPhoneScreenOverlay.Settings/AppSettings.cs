namespace VRPhoneScreenOverlay.Settings;

public enum ControllerHandPreference
{
    Right,
    Left,
}

public enum UpdateChannel
{
    Stable,
    Beta,
}

public sealed record AppSettings(
    int SchemaVersion,
    ControllerHandPreference ControllerHand,
    int VideoResolutionPercent,
    int VideoBitrateMbps,
    int VideoMaximumFramesPerSecond,
    UpdateChannel UpdateChannel,
    bool KeepAwakeWhileGrabbed,
    bool VrUnlockKeypadEnabled,
    bool AutoOpenPhoneOverlay = false,
    bool AutoTurnOffPhoneScreen = false,
    bool LaunchWithSteamVr = false,
    bool MinimizeAfterOverlayOpened = false,
    float PlayspaceFlingStrength = 1,
    float PlayspaceGravity = 9.8f,
    float PlayspaceFriction = 0,
    bool PlayspaceResetAllOffsets = false,
    bool PicoMicrophoneKeeperEnabled = false,
    float PlayspaceMultiplier = 1,
    bool PlayspaceInertiaEnabled = false)
{
    public const int CurrentSchemaVersion = 8;

    public static AppSettings Default { get; } = new(
        CurrentSchemaVersion,
        ControllerHandPreference.Right,
        100,
        16,
        60,
        UpdateChannel.Beta,
        false,
        false);
}

public sealed record AppSettingsSnapshot(
    AppSettings Value,
    string ReasonCode,
    string Message,
    bool IsPersisted);

public sealed class AppSettingsChangedEventArgs(AppSettingsSnapshot snapshot) : EventArgs
{
    public AppSettingsSnapshot Snapshot { get; } = snapshot;
}

public interface IAppSettingsService : IDisposable
{
    public AppSettingsSnapshot Snapshot { get; }

    public event EventHandler<AppSettingsChangedEventArgs>? Changed;

    public ValueTask InitializeAsync(CancellationToken cancellationToken);

    public ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken);

    // Non-motion changes must preserve live motion values at the persistence boundary.
    public ValueTask SaveNonMotionAsync(AppSettings settings, CancellationToken cancellationToken) =>
        SaveAsync(MotionPreferences.From(Snapshot.Value).Apply(settings), cancellationToken);

    public ValueTask SaveMotionAsync(MotionPreferences motion, CancellationToken cancellationToken) =>
        SaveAsync(motion.Apply(Snapshot.Value), cancellationToken);

    public ValueTask ToggleScreenGuardAsync(CancellationToken cancellationToken) =>
        SaveNonMotionAsync(Snapshot.Value with { AutoTurnOffPhoneScreen = !Snapshot.Value.AutoTurnOffPhoneScreen }, cancellationToken);

    public ValueTask SaveChoicesAsync(AppSettings choices, CancellationToken cancellationToken) =>
        SaveNonMotionAsync(choices, cancellationToken);
}
