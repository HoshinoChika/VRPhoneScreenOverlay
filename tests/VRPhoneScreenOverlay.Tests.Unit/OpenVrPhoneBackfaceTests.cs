using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPhoneBackfaceTests
{
    [Fact]
    public void BackfaceFacesAwayAndStaysBehindTheFrontAfterRotation()
    {
        HmdMatrix34_t front = OpenVrTransformMath.Identity();
        front.m0 = 0;
        front.m2 = 1;
        front.m8 = -1;
        front.m10 = 0;
        front.m3 = 2;
        HmdMatrix34_t back = OpenVrPhoneBackface.BackTransform(front);
        Assert.Equal(-front.m2, back.m2);
        Assert.Equal(-front.m8, back.m8);
        Assert.Equal(1.999f, back.m3, 4);
        Assert.Equal(front.m7, back.m7);
    }
}
