using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrSceneOverlayTests
{
    [Theory]
    [InlineData(1080, 2400, 0.65f)]
    [InlineData(2400, 1080, 1.4444444f)]
    [InlineData(1000, 1000, 0.65f)]
    public void KeepsPhysicalShortEdgeAcrossOrientation(
        int frameWidth,
        int frameHeight,
        float expectedWidthMeters)
    {
        float widthMeters = OpenVrSceneOverlay.CalculateOverlayWidthMeters(
            0.65f,
            frameWidth,
            frameHeight);

        Assert.Equal(expectedWidthMeters, widthMeters, 5);
    }
}
