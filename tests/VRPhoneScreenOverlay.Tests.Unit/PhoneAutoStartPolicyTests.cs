using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneAutoStartPolicyTests
{
    [Fact]
    public void ExplicitIntentRestoresTheRetryBudgetAndClearsManualSuppression()
    {
        PhoneAutoStartPolicy policy = new();
        policy.Suppress();
        Assert.False(policy.TryBegin(true, true, true, true));
        policy.Reset();
        for (int attempt = 0; attempt < 3; attempt++) { Assert.True(policy.TryBegin(true, true, true, true)); }
        Assert.False(policy.TryBegin(true, true, true, true));
    }

    [Fact]
    public void WaitsForBothConnectionsAndNeverRestartsAfterManualStop()
    {
        PhoneAutoStartPolicy policy = new();
        Assert.False(policy.TryBegin(true, false, true, true));
        Assert.False(policy.TryBegin(true, true, false, true));
        Assert.False(policy.TryBegin(false, true, true, true));
        Assert.False(policy.TryBegin(true, true, true, false));
        Assert.True(policy.TryBegin(true, true, true, true));
        policy.Suppress();
        Assert.False(policy.TryBegin(false, false, false, true));
        Assert.False(policy.TryBegin(true, true, true, true));
        Assert.True(new PhoneAutoStartPolicy().TryBegin(true, true, true, true));
    }

    [Fact]
    public void RepeatedFailuresHaveABoundedRetryBudget()
    {
        PhoneAutoStartPolicy policy = new();
        for (int attempt = 0; attempt < 3; attempt++) { Assert.True(policy.TryBegin(true, true, true, true)); }
        Assert.False(policy.TryBegin(true, true, true, true));
    }
}
