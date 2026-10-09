using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.App;

internal static class SettingsControlPolicy
{
    public static AppSettings RestoreVideoDraft(AppSettings saved, AppSettings draft) => saved with
    {
        VideoResolutionPercent = draft.VideoResolutionPercent,
        VideoBitrateMbps = draft.VideoBitrateMbps,
        VideoMaximumFramesPerSecond = draft.VideoMaximumFramesPerSecond,
    };
    public static bool CanUseResolutionSelector(
        bool generalOperationInProgress,
        int nativeWidth,
        int nativeHeight) =>
        !generalOperationInProgress && nativeWidth > 0 && nativeHeight > 0;

    public static bool CanUsePendingSettingsActions(
        bool generalOperationInProgress,
        bool immediateSettingsInProgress,
        bool settingsDirty) =>
        !generalOperationInProgress && !immediateSettingsInProgress && settingsDirty;
}
