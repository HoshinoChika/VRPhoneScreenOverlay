using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPointerPoseResolverTests
{
    [Fact]
    public void RejectsUncalibratedControllerPose()
    {
        OpenVrPointerPoseResolver resolver = new();

        bool resolved = resolver.TryResolve(
            false,
            default,
            true,
            OpenVrTransformMath.Identity(),
            out _);

        Assert.False(resolved);
    }

    [Fact]
    public void ReconstructsPointerFromCalibratedControllerPose()
    {
        OpenVrPointerPoseResolver resolver = new();
        HmdMatrix34_t controller = OpenVrTransformMath.Identity();
        controller.m3 = 1;
        HmdMatrix34_t controllerToPointer = OpenVrTransformMath.Identity();
        controllerToPointer.m11 = -0.12f;
        HmdMatrix34_t actionPointer = OpenVrTransformMath.Multiply(
            controller,
            controllerToPointer);

        Assert.True(resolver.TryResolve(
            true,
            actionPointer,
            true,
            controller,
            out HmdMatrix34_t direct));
        Assert.Equal(actionPointer.m3, direct.m3, 5);
        Assert.Equal(actionPointer.m11, direct.m11, 5);

        controller.m3 = 2;
        Assert.True(resolver.TryResolve(
            false,
            default,
            true,
            controller,
            out HmdMatrix34_t reconstructed));
        Assert.Equal(2, reconstructed.m3, 5);
        Assert.Equal(-0.12f, reconstructed.m11, 5);
    }
}
