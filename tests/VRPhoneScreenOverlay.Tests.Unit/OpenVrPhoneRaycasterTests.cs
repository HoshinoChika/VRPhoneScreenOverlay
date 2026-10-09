using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPhoneRaycasterTests
{
    [Fact]
    public void HitsCenterOfFiniteOverlay()
    {
        HmdMatrix34_t pointer = OpenVrTransformMath.Identity();
        HmdMatrix34_t overlay = OpenVrTransformMath.Identity();
        overlay.m11 = -1;

        bool intersects = OpenVrPhoneRaycaster.TryIntersect(
            pointer,
            overlay,
            0.6f,
            0.45f,
            out VROverlayIntersectionResults_t hit);

        Assert.True(intersects);
        Assert.Equal(1, hit.fDistance, 5);
        Assert.Equal(0.5f, hit.vUVs.v0, 5);
        Assert.Equal(0.5f, hit.vUVs.v1, 5);
    }

    [Fact]
    public void RejectsInfinitePlaneOutsidePhoneRectangle()
    {
        HmdMatrix34_t pointer = OpenVrTransformMath.Identity();
        pointer.m3 = 0.31f;
        HmdMatrix34_t overlay = OpenVrTransformMath.Identity();
        overlay.m11 = -1;

        bool intersects = OpenVrPhoneRaycaster.TryIntersect(
            pointer,
            overlay,
            0.6f,
            0.45f,
            out _);

        Assert.False(intersects);
    }

    [Fact]
    public void AllowsSmallFloatingPointErrorAtRightEdge()
    {
        HmdMatrix34_t pointer = OpenVrTransformMath.Identity();
        pointer.m3 = 0.30005f;
        HmdMatrix34_t overlay = OpenVrTransformMath.Identity();
        overlay.m11 = -1;

        bool intersects = OpenVrPhoneRaycaster.TryIntersect(
            pointer,
            overlay,
            0.6f,
            0.45f,
            out VROverlayIntersectionResults_t hit);

        Assert.True(intersects);
        Assert.Equal(1, hit.vUVs.v0, 5);
    }

    [Fact]
    public void AllowsSmallFloatingPointErrorAtBottomEdge()
    {
        HmdMatrix34_t pointer = OpenVrTransformMath.Identity();
        pointer.m7 = -0.66670f;
        HmdMatrix34_t overlay = OpenVrTransformMath.Identity();
        overlay.m11 = -1;

        bool intersects = OpenVrPhoneRaycaster.TryIntersect(
            pointer,
            overlay,
            0.6f,
            0.45f,
            out VROverlayIntersectionResults_t hit);

        Assert.True(intersects);
        Assert.Equal(0, hit.vUVs.v1, 5);
    }

    [Fact]
    public void RejectsFarHitWhenTransformAxesAreMalformed()
    {
        HmdMatrix34_t pointer = OpenVrTransformMath.Identity();
        pointer.m3 = 10;
        HmdMatrix34_t overlay = OpenVrTransformMath.Identity();
        overlay.m0 = 0.01f;
        overlay.m5 = 0.01f;
        overlay.m11 = -1;

        bool intersects = OpenVrPhoneRaycaster.TryIntersect(
            pointer,
            overlay,
            0.6f,
            0.45f,
            out _);

        Assert.False(intersects);
    }

    [Fact]
    public void RejectsOverlayBehindPointer()
    {
        HmdMatrix34_t pointer = OpenVrTransformMath.Identity();
        HmdMatrix34_t overlay = OpenVrTransformMath.Identity();
        overlay.m11 = 1;

        bool intersects = OpenVrPhoneRaycaster.TryIntersect(
            pointer,
            overlay,
            0.6f,
            0.45f,
            out _);

        Assert.False(intersects);
    }
}
