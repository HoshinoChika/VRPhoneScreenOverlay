using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPhoneScaleRangeTests
{
    [Theory]
    [InlineData(0.1f, 0.2f)]
    [InlineData(1f, 1f)]
    [InlineData(3f, 2.5f)]
    public void ClampsScaleWithoutChangingMaximum(float requested, float expected)
    {
        Assert.Equal(expected, OpenVrPhoneScaleRange.Clamp(requested));
    }
}
