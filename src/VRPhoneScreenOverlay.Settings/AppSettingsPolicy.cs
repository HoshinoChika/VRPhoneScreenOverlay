namespace VRPhoneScreenOverlay.Settings;

public static class AppSettingsPolicy
{
    private static readonly int[] _bitrates = [8, 12, 16, 24, 32];
    private static readonly int[] _frameRates = [25, 30, 45, 60];

    public static IReadOnlyList<int> VideoBitratesMbps => _bitrates;

    public static IReadOnlyList<int> VideoMaximumFrameRates => _frameRates;

    public static AppSettings Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        AppSettings defaults = AppSettings.Default;
        return settings with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            ControllerHand = Enum.IsDefined(settings.ControllerHand)
                ? settings.ControllerHand
                : defaults.ControllerHand,
            VideoResolutionPercent = settings.VideoResolutionPercent is >= 1 and <= 100
                ? settings.VideoResolutionPercent
                : defaults.VideoResolutionPercent,
            VideoBitrateMbps = _bitrates.Contains(settings.VideoBitrateMbps)
                ? settings.VideoBitrateMbps
                : defaults.VideoBitrateMbps,
            VideoMaximumFramesPerSecond = _frameRates.Contains(
                settings.VideoMaximumFramesPerSecond)
                ? settings.VideoMaximumFramesPerSecond
                : defaults.VideoMaximumFramesPerSecond,
            PlayspaceMultiplier = float.IsFinite(settings.PlayspaceMultiplier) && settings.PlayspaceMultiplier is >= 1 and <= 40
                ? MathF.Round(settings.PlayspaceMultiplier) : defaults.PlayspaceMultiplier,
            PlayspaceFlingStrength = float.IsFinite(settings.PlayspaceFlingStrength) && settings.PlayspaceFlingStrength is >= 0 and <= 20
                ? settings.PlayspaceFlingStrength : defaults.PlayspaceFlingStrength,
            PlayspaceGravity = float.IsFinite(settings.PlayspaceGravity) && settings.PlayspaceGravity is >= 0 and <= 30
                ? settings.PlayspaceGravity : defaults.PlayspaceGravity,
            PlayspaceFriction = float.IsFinite(settings.PlayspaceFriction) && settings.PlayspaceFriction is >= 0 and <= 999
                ? settings.PlayspaceFriction : defaults.PlayspaceFriction,
            UpdateChannel = UpdateChannel.Beta,
        };
    }
}
