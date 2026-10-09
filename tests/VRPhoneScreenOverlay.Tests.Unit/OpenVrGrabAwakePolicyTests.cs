using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrGrabAwakePolicyTests
{
    [Fact]
    public void EveryGrabStartsWithOneWakeEvenWhenPeriodicKeepAwakeIsDisabled()
    {
        OpenVrGrabAwakePolicy policy = new();

        Assert.Equal(OpenVrGrabAwakeAction.Wake, policy.NextAction(true, false, 1_000));
        Assert.Equal(OpenVrGrabAwakeAction.None, policy.NextAction(true, false, 10_000));
        Assert.Equal(OpenVrGrabAwakeAction.None, policy.NextAction(false, false, 10_100));
        Assert.Equal(OpenVrGrabAwakeAction.Wake, policy.NextAction(true, false, 10_200));
    }

    [Fact]
    public void EnabledPolicySendsStatelessUserActivityWhileGrabbed()
    {
        OpenVrGrabAwakePolicy policy = new();

        Assert.Equal(OpenVrGrabAwakeAction.Wake, policy.NextAction(true, true, 1_000));
        Assert.Equal(OpenVrGrabAwakeAction.None, policy.NextAction(true, true, 3_999));
        Assert.Equal(OpenVrGrabAwakeAction.UserActivity, policy.NextAction(true, true, 4_000));
        Assert.Equal(OpenVrGrabAwakeAction.None, policy.NextAction(false, true, 7_000));
        Assert.Equal(OpenVrGrabAwakeAction.None, policy.NextAction(false, true, 10_000));
    }
}
