using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidDisplaySizeTests
{
    [Fact]
    public void OverrideDisplaySizeTakesPriorityOverPhysicalSize()
    {
        bool parsed = AndroidDisplaySize.TryParseWmSize(
            "Physical size: 1080x2400\nOverride size: 720x1600\n",
            out AndroidDisplaySize size);

        Assert.True(parsed);
        Assert.Equal(new AndroidDisplaySize(720, 1600), size);
    }

    [Theory]
    [InlineData(864, 1920, 1080, 2400)]
    [InlineData(1920, 864, 2400, 1080)]
    public void VideoCoordinatesMapToNativeDisplayOrientation(
        int videoWidth,
        int videoHeight,
        int expectedWidth,
        int expectedHeight)
    {
        AndroidDisplaySize display = new(1080, 2400);
        PhoneInputCommand input = new(
            1,
            PhoneInputCommandKind.PointerDown,
            -2,
            0.5f,
            1f,
            videoWidth,
            videoHeight,
            DateTimeOffset.UtcNow);

        PhoneInputCommand mapped = display.Map(input);

        Assert.Equal(expectedWidth, mapped.ScreenWidth);
        Assert.Equal(expectedHeight, mapped.ScreenHeight);
        Assert.Equal(1f, mapped.NormalizedY);
    }
}
