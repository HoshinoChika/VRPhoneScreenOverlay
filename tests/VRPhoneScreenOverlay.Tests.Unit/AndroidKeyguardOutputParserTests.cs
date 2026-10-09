using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidKeyguardOutputParserTests
{
    [Theory]
    [InlineData("  showing=true", AndroidKeyguardState.Locked)]
    [InlineData("mKeyguardState={showing=true, secure=true}", AndroidKeyguardState.Locked)]
    [InlineData("isStatusBarKeyguard=true", AndroidKeyguardState.Locked)]
    [InlineData("mShowingLockscreen=true", AndroidKeyguardState.Locked)]
    [InlineData("KeyguardServiceDelegate showing=false", AndroidKeyguardState.Unlocked)]
    [InlineData("isStatusBarKeyguard=false", AndroidKeyguardState.Unlocked)]
    public void ParsesKnownPolicyFormats(string output, AndroidKeyguardState expected)
    {
        Assert.Equal(expected, AndroidKeyguardOutputParser.Parse(output));
    }

    [Fact]
    public void DoesNotTreatUnrelatedShowingFlagAsKeyguard()
    {
        Assert.Equal(
            AndroidKeyguardState.Unknown,
            AndroidKeyguardOutputParser.Parse("DreamManager showing=true"));
    }
}
