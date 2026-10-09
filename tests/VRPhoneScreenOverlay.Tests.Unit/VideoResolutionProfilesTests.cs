using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class VideoResolutionProfilesTests
{
    [Fact]
    public void CommonPortraitResolutionProducesExpectedDynamicChoices()
    {
        IReadOnlyList<VideoResolutionProfile> profiles =
            VideoResolutionProfiles.Create(1080, 2400);

        Assert.Collection(
            profiles,
            profile => AssertProfile(profile, 100, 1080, 2400, 0, false),
            profile => AssertProfile(profile, 90, 972, 2160, 2160, false),
            profile => AssertProfile(profile, 80, 864, 1920, 1920, false),
            profile => AssertProfile(profile, 70, 756, 1680, 1680, false),
            profile => AssertProfile(profile, 67, 720, 1600, 1600, true));
    }

    [Fact]
    public void DisplayDimensionsSwapForLandscapeWithoutChangingPercentages()
    {
        IReadOnlyList<VideoResolutionProfile> profiles =
            VideoResolutionProfiles.Create(2400, 1080);

        Assert.Equal((2400, 1080), (profiles[0].ExpectedWidth, profiles[0].ExpectedHeight));
        Assert.Equal((2160, 972), (profiles[1].ExpectedWidth, profiles[1].ExpectedHeight));
    }

    [Fact]
    public void DeviceBelowMinimumShortEdgeOnlyOffersNative()
    {
        VideoResolutionProfile profile = Assert.Single(
            VideoResolutionProfiles.Create(640, 1280));

        AssertProfile(profile, 100, 640, 1280, 0, false);
    }

    [Fact]
    public void UnavailableSavedPercentResolvesToNearestSafeChoice()
    {
        VideoResolutionProfile profile = VideoResolutionProfiles.Resolve(1080, 2400, 65);

        Assert.Equal(67, profile.Percent);
        Assert.True(profile.IsApproximate);
    }

    private static void AssertProfile(
        VideoResolutionProfile profile,
        int percent,
        int width,
        int height,
        int maximumSize,
        bool approximate)
    {
        Assert.Equal(percent, profile.Percent);
        Assert.Equal(width, profile.ExpectedWidth);
        Assert.Equal(height, profile.ExpectedHeight);
        Assert.Equal(maximumSize, profile.MaximumSize);
        Assert.Equal(approximate, profile.IsApproximate);
    }
}
