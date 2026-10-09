using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrTransformMathTests
{
    [Fact]
    public void RigidTransformMultipliedByItsInverseIsIdentity()
    {
        HmdMatrix34_t transform = new()
        {
            m0 = 0,
            m2 = 1,
            m3 = 1.25f,
            m5 = 1,
            m7 = -0.4f,
            m8 = -1,
            m11 = 2.1f,
        };

        HmdMatrix34_t result = OpenVrTransformMath.Multiply(
            transform,
            OpenVrTransformMath.InverseRigid(transform));

        Assert.Equal(1, result.m0, 5);
        Assert.Equal(1, result.m5, 5);
        Assert.Equal(1, result.m10, 5);
        Assert.Equal(0, result.m3, 5);
        Assert.Equal(0, result.m7, 5);
        Assert.Equal(0, result.m11, 5);
    }

    [Fact]
    public void GrabSmoothingMovesTowardTargetWithoutOvershooting()
    {
        HmdMatrix34_t current = OpenVrTransformMath.Identity();
        HmdMatrix34_t target = OpenVrTransformMath.Identity();
        target.m3 = 1;
        target.m7 = -0.5f;
        target.m11 = -2;

        HmdMatrix34_t result = OpenVrTransformMath.Smooth(current, target, 0.016f);

        Assert.InRange(result.m3, 0.001f, 0.999f);
        Assert.InRange(result.m7, -0.499f, -0.001f);
        Assert.InRange(result.m11, -1.999f, -0.001f);
        Assert.Equal(1, result.m0, 5);
        Assert.Equal(1, result.m5, 5);
        Assert.Equal(1, result.m10, 5);
    }
}
