using VRPhoneScreenOverlay.Presentation;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OverlayWindowPolicyTests
{
    [Fact]
    public void MinimizeOnlyOnSuccessfulOpenAndNeverDuringRepeatedUpdatesOrQualityRestart()
    {
        OverlayWindowPolicy policy = new();
        Assert.False(policy.Observe(false, false, true));
        Assert.True(policy.Observe(true, false, true));
        Assert.False(policy.Observe(true, false, true));
        Assert.False(policy.Observe(false, false, true));
        Assert.False(policy.Observe(true, false, true));
        Assert.False(policy.Observe(false, true, true));
        Assert.True(policy.Observe(true, false, true));
    }

    [Fact]
    public void EnablingPreferenceDuringAnOpenSessionWaitsUntilNextOpen()
    {
        OverlayWindowPolicy policy = new();
        Assert.False(policy.Observe(true, false, false));
        Assert.False(policy.Observe(true, false, true));
        Assert.False(policy.Observe(false, true, true));
        Assert.True(policy.Observe(true, false, true));
    }
}
