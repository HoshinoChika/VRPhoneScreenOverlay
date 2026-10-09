using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPointerFilterTests
{
    [Fact]
    public void IgnoresTinyJitterAndTracksIntentionalMovement()
    {
        OpenVrPointerSmoother smoother = new();
        VROverlayIntersectionResults_t first = CreateHit(0.4f, 0.4f);
        VROverlayIntersectionResults_t jitter = CreateHit(0.4005f, 0.4005f);
        VROverlayIntersectionResults_t moved = CreateHit(0.6f, 0.6f);

        VROverlayIntersectionResults_t initial = smoother.Update(first, 1_000);
        VROverlayIntersectionResults_t filteredJitter = smoother.Update(jitter, 1_016);
        VROverlayIntersectionResults_t filteredMove = smoother.Update(moved, 1_032);

        Assert.Equal(0.4f, initial.vUVs.v0);
        Assert.Equal(0.4f, filteredJitter.vUVs.v0);
        Assert.InRange(filteredMove.vUVs.v0, 0.4001f, 0.5999f);

        smoother.Reset();
        Assert.Equal(0.6f, smoother.Update(moved, 2_000).vUVs.v0);
    }

    private static VROverlayIntersectionResults_t CreateHit(float u, float v) => new()
    {
        vPoint = new HmdVector3_t { v0 = u, v1 = v, v2 = -1 },
        vNormal = new HmdVector3_t { v2 = 1 },
        vUVs = new HmdVector2_t { v0 = u, v1 = v },
        fDistance = 1,
    };
}
