using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidCapabilityEvaluatorTests
{
    [Theory]
    [InlineData(21, true, false)]
    [InlineData(29, true, false)]
    [InlineData(30, true, true)]
    [InlineData(35, true, true)]
    public void CapabilitiesDependOnPlatformApiNotVendor(
        int sdk,
        bool videoAvailable,
        bool audioAvailable)
    {
        AndroidDeviceCapabilities capabilities = AndroidCapabilityEvaluator.Evaluate(sdk);

        Assert.Equal(videoAvailable, capabilities.Video.IsAvailable);
        Assert.Equal(videoAvailable, capabilities.Control.IsAvailable);
        Assert.Equal(audioAvailable, capabilities.InternalAudio.IsAvailable);
    }

    [Fact]
    public void UnknownSdkProducesExplicitUnknownCapabilities()
    {
        AndroidDeviceCapabilities capabilities = AndroidCapabilityEvaluator.Evaluate(null);

        Assert.Equal(AndroidCapabilityState.Unknown, capabilities.Video.State);
        Assert.Equal(AndroidCapabilityState.Unknown, capabilities.InternalAudio.State);
    }

}
