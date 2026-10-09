using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class VrVideoMenuTests
{
    [Fact]
    public void VideoDraftOnlySubmitsOnApplyAndBusyRejectsEdits()
    {
        Assert.Equal(OpenVrMenuTarget.None, default(OpenVrMenuTarget));
        OpenVrVideoSettingsChannel channel = new();
        channel.ConfigureChoices(VRPhoneScreenOverlay.Settings.AppSettingsPolicy.VideoBitratesMbps,
            VRPhoneScreenOverlay.Settings.AppSettingsPolicy.VideoMaximumFrameRates);
        channel.Publish(new(100, 16, 60), [70, 80, 90, 100]);
        OpenVrPhoneMenuState state = new() { VideoSettings = channel };
        state.SetEnvironment(true, false, false);
        Click(OpenVrMenuTarget.Dot);
        Click(OpenVrMenuTarget.VideoSettings);
        Assert.True(state.VideoExpanded);
        Click(OpenVrMenuTarget.ResolutionDown);
        Click(OpenVrMenuTarget.BitrateUp);
        Click(OpenVrMenuTarget.FrameRateDown);
        Assert.Equal(new(90, 24, 45), state.VideoDraft);
        Assert.Null(channel.Take());
        Click(OpenVrMenuTarget.VideoApply);
        Assert.Equal(state.VideoDraft, channel.Take());
        Click(OpenVrMenuTarget.BitrateUp);
        Assert.Equal(24, state.VideoDraft.Bitrate);
        Click(OpenVrMenuTarget.VideoBack);
        Assert.False(state.VideoExpanded);
        Assert.True(state.Expanded);

        void Click(OpenVrMenuTarget target)
        {
            state.ProcessInput(true, true, target, 0, false, default);
            state.ProcessInput(true, true, target, 0, true, default);
        }
    }

    [Theory]
    [InlineData(40, 40, (int)OpenVrMenuTarget.VideoBack)]
    [InlineData(190, 120, (int)OpenVrMenuTarget.ResolutionDown)]
    [InlineData(320, 120, (int)OpenVrMenuTarget.ResolutionUp)]
    [InlineData(190, 200, (int)OpenVrMenuTarget.BitrateDown)]
    [InlineData(320, 200, (int)OpenVrMenuTarget.BitrateUp)]
    [InlineData(190, 280, (int)OpenVrMenuTarget.FrameRateDown)]
    [InlineData(320, 280, (int)OpenVrMenuTarget.FrameRateUp)]
    [InlineData(180, 390, (int)OpenVrMenuTarget.VideoApply)]
    public void VideoPageTargetsMatchRows(int x, int y, int expected)
    {
        Assert.True(OpenVrPhoneMenuLayout.TryMap(x / 360f, y / 640f, false, out var target, out _, video: true));
        Assert.Equal((OpenVrMenuTarget)expected, target);
    }

    [Fact]
    public void RootVideoEntrySitsImmediatelyBelowHide()
    {
        Assert.True(OpenVrPhoneMenuLayout.TryMap(0.5f, 110 / 640f, false, out var target, out _));
        Assert.Equal(OpenVrMenuTarget.VideoSettings, target);
    }
}
