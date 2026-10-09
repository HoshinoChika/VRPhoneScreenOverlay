using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class SettingsControlPolicyTests
{
    [Fact]
    public void HandSwitchKeepsUnsavedVideoDraftWithoutUndoingPersistedHandAndToggles()
    {
        AppSettings draft = AppSettings.Default with
        {
            VideoResolutionPercent = 75,
            VideoBitrateMbps = 24,
            VideoMaximumFramesPerSecond = 45,
        };
        AppSettings saved = AppSettings.Default with
        {
            ControllerHand = ControllerHandPreference.Left,
            AutoOpenPhoneOverlay = true,
        };
        AppSettings restored = SettingsControlPolicy.RestoreVideoDraft(saved, draft);
        Assert.Equal(ControllerHandPreference.Left, restored.ControllerHand);
        Assert.True(restored.AutoOpenPhoneOverlay);
        Assert.Equal(75, restored.VideoResolutionPercent);
        Assert.Equal(24, restored.VideoBitrateMbps);
        Assert.Equal(45, restored.VideoMaximumFramesPerSecond);
        Assert.Equal(100, saved.VideoResolutionPercent);
        Assert.Equal(16, saved.VideoBitrateMbps);
    }
    [Fact]
    public void ImmediateSettingSaveDoesNotDisableKnownResolutionProfiles()
    {
        Assert.True(SettingsControlPolicy.CanUseResolutionSelector(
            generalOperationInProgress: false,
            nativeWidth: 1080,
            nativeHeight: 2400));
        Assert.False(SettingsControlPolicy.CanUsePendingSettingsActions(
            generalOperationInProgress: false,
            immediateSettingsInProgress: true,
            settingsDirty: true));
    }

    [Theory]
    [InlineData(true, 1080, 2400)]
    [InlineData(false, 0, 2400)]
    [InlineData(false, 1080, 0)]
    public void ResolutionSelectorRequiresIdleOperationAndKnownPhoneSize(
        bool generalOperationInProgress,
        int nativeWidth,
        int nativeHeight)
    {
        Assert.False(SettingsControlPolicy.CanUseResolutionSelector(
            generalOperationInProgress,
            nativeWidth,
            nativeHeight));
    }
}
