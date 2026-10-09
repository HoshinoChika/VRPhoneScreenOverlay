using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPhoneGroupTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryComponentFollowsTheSameRigidMotion(int component)
    {
        OpenVrPhoneSurface surface = (OpenVrPhoneSurface)component;
        HmdMatrix34_t root = OpenVrTransformMath.Identity();
        root.m11 = -1.1f;
        HmdMatrix34_t movement = new() { m2 = 1, m3 = 2, m5 = 1, m7 = 0.3f, m8 = -1, m11 = 1 };
        HmdMatrix34_t before = OpenVrPhoneGroupLayout.SurfacePose(surface, root, 0.65f, 0.5f, false);
        HmdMatrix34_t after = OpenVrPhoneGroupLayout.SurfacePose(surface,
            OpenVrTransformMath.Multiply(movement, root), 0.65f, 0.5f, false);
        HmdMatrix34_t expected = OpenVrTransformMath.Multiply(movement, before);
        Assert.Equal(expected.m3, after.m3, 5);
        Assert.Equal(expected.m7, after.m7, 5);
        Assert.Equal(expected.m11, after.m11, 5);
        Assert.Equal(expected.m2, after.m2);
        Assert.Equal(expected.m8, after.m8);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ScalingChangesOnlyPhoneSizeAndKeepsTheGrabbedPointUnderTheHand(int component)
    {
        OpenVrPhoneSurface surface = (OpenVrPhoneSurface)component;
        HmdMatrix34_t root = OpenVrTransformMath.Identity();
        root.m11 = -1;
        HmdVector3_t point = OpenVrPhoneGroupLayout.Point(surface, root, 0.65f, 0.5f, false, 0.3f, 0.7f);
        HmdMatrix34_t scaled = OpenVrPhoneGroupLayout.AnchorScale(surface, root, 0.65f, 1.3f, 0.5f, false, 0.3f, 0.7f);
        HmdVector3_t anchored = OpenVrPhoneGroupLayout.Point(surface, scaled, 1.3f, 0.5f, false, 0.3f, 0.7f);
        Assert.Equal(point.v0, anchored.v0, 5);
        Assert.Equal(point.v1, anchored.v1, 5);
        Assert.Equal(point.v2, anchored.v2, 5);
        float before = Width(surface, root, 0.65f);
        float after = Width(surface, scaled, 1.3f);
        Assert.Equal(surface == OpenVrPhoneSurface.Phone ? before * 2 : before, after, 5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RecalledControlsKeepTheirSizeAndPositionWhileTheHiddenPhoneIsScaled(int component)
    {
        HmdMatrix34_t root = OpenVrTransformMath.Identity();
        HmdMatrix34_t scaled = OpenVrPhoneGroupLayout.AnchorScale((OpenVrPhoneSurface)component,
            root, 0.65f, 1.3f, 0.5f, true, 0.2f, 0.8f);
        Assert.Equal(root, scaled);
    }

    private static float Width(OpenVrPhoneSurface surface, HmdMatrix34_t root, float phoneWidth)
    {
        HmdVector3_t left = OpenVrPhoneGroupLayout.Point(surface, root, phoneWidth, 0.5f, false, 0, 0.5f);
        HmdVector3_t right = OpenVrPhoneGroupLayout.Point(surface, root, phoneWidth, 0.5f, false, 1, 0.5f);
        return MathF.Sqrt(MathF.Pow(right.v0 - left.v0, 2) + MathF.Pow(right.v1 - left.v1, 2) + MathF.Pow(right.v2 - left.v2, 2));
    }
}
