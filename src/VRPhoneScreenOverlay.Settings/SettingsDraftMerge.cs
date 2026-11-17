namespace VRPhoneScreenOverlay.Settings;

public static class SettingsDraftMerge
{
    // Shared changes win in their own fields; unrelated unapplied desktop choices survive.
    public static AppSettings Merge(AppSettings draft, AppSettings previous, AppSettings current) => current with
    {
        ControllerHand = current.ControllerHand != previous.ControllerHand ? current.ControllerHand : draft.ControllerHand,
        VideoResolutionPercent = current.VideoResolutionPercent != previous.VideoResolutionPercent ? current.VideoResolutionPercent : draft.VideoResolutionPercent,
        VideoBitrateMbps = current.VideoBitrateMbps != previous.VideoBitrateMbps ? current.VideoBitrateMbps : draft.VideoBitrateMbps,
        VideoMaximumFramesPerSecond = current.VideoMaximumFramesPerSecond != previous.VideoMaximumFramesPerSecond ? current.VideoMaximumFramesPerSecond : draft.VideoMaximumFramesPerSecond,
        VideoUploadMode = current.VideoUploadMode != previous.VideoUploadMode ? current.VideoUploadMode : draft.VideoUploadMode,
        UpdateChannel = current.UpdateChannel != previous.UpdateChannel ? current.UpdateChannel : draft.UpdateChannel,
    };
}
