namespace VRPhoneScreenOverlay.Settings;

/// <summary>Compensate only fields changed by this operation and still holding its value.</summary>
public static class SettingsChoiceRollback
{
    public static AppSettings Restore(AppSettings current, AppSettings attempted, AppSettings previous) => current with
    {
        ControllerHand = attempted.ControllerHand != previous.ControllerHand && current.ControllerHand == attempted.ControllerHand ? previous.ControllerHand : current.ControllerHand,
        VideoResolutionPercent = attempted.VideoResolutionPercent != previous.VideoResolutionPercent && current.VideoResolutionPercent == attempted.VideoResolutionPercent ? previous.VideoResolutionPercent : current.VideoResolutionPercent,
        VideoBitrateMbps = attempted.VideoBitrateMbps != previous.VideoBitrateMbps && current.VideoBitrateMbps == attempted.VideoBitrateMbps ? previous.VideoBitrateMbps : current.VideoBitrateMbps,
        VideoMaximumFramesPerSecond = attempted.VideoMaximumFramesPerSecond != previous.VideoMaximumFramesPerSecond && current.VideoMaximumFramesPerSecond == attempted.VideoMaximumFramesPerSecond ? previous.VideoMaximumFramesPerSecond : current.VideoMaximumFramesPerSecond,
        UpdateChannel = attempted.UpdateChannel != previous.UpdateChannel && current.UpdateChannel == attempted.UpdateChannel ? previous.UpdateChannel : current.UpdateChannel,
    };
}
