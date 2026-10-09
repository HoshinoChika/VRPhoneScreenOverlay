using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrInteractionGateTests
{
    [Fact]
    public void PhoneActionsEnableOnlyAfterTwoReleasedPollsOverTarget()
    {
        OpenVrPhoneActionSetGate gate = new();

        Assert.False(gate.ShouldEnable(true, true, false));
        Assert.True(gate.ShouldEnable(true, true, false));
    }

    [Fact]
    public void HeldOutsidePressCannotEnableAfterEnteringTarget()
    {
        OpenVrPhoneActionSetGate gate = new();

        Assert.False(gate.ShouldEnable(false, true, true));
        Assert.False(gate.ShouldEnable(true, true, true));
        Assert.False(gate.ShouldEnable(true, true, false));
        Assert.True(gate.ShouldEnable(true, true, false));
    }

    [Fact]
    public void MissingRawStateFallsBackAfterThreePolls()
    {
        OpenVrPhoneActionSetGate gate = new();

        Assert.False(gate.ShouldEnable(true, false, false));
        Assert.False(gate.ShouldEnable(true, false, false));
        Assert.True(gate.ShouldEnable(true, false, false));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    public void PhoneSystemCommandRequiresNewPressEdgeOverVisiblePhone(
        bool acceptsButtons,
        bool currentPressed,
        bool previousPressed,
        bool expected)
    {
        Assert.Equal(
            expected,
            OpenVrInteractionGate.ShouldEmitPhoneButton(
                acceptsButtons,
                currentPressed,
                previousPressed));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void PhoneButtonsRequireVisibleHitWithoutGrab(
        bool phoneHit,
        bool grabbed,
        bool expected)
    {
        Assert.Equal(
            expected,
            OpenVrInteractionGate.CanAcceptPhoneButtons(phoneHit, grabbed));
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, false)]
    public void PhoneTargetRequiresDisplayedPointer(
        bool phoneHit,
        bool pointerVisible,
        bool grabbed,
        bool expected)
    {
        Assert.Equal(
            expected,
            OpenVrInteractionGate.IsVisiblePhoneTarget(
                phoneHit,
                pointerVisible,
                grabbed));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void PhoneHitRequiresFiniteRayIntersection(
        bool rayIntersectsPhone,
        bool alreadyGrabbed,
        bool expected)
    {
        bool result = OpenVrInteractionGate.IsPhoneHit(
            rayIntersectsPhone,
            alreadyGrabbed);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(true, false, false, true, true, true)]
    [InlineData(true, false, false, false, true, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(false, false, false, true, true, false)]
    public void GrabStartsOnlyOnPressEdgeOverVisiblePhone(
        bool grabPressed,
        bool previousGrabPressed,
        bool alreadyGrabbed,
        bool visiblePhoneHit,
        bool controllerPoseValid,
        bool expected)
    {
        bool result = OpenVrInteractionGate.CanStartGrab(
            grabPressed,
            previousGrabPressed,
            alreadyGrabbed,
            visiblePhoneHit,
            controllerPoseValid);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    public void GrabEndsBeforeHoverIsEvaluated(
        bool grabPressed,
        bool alreadyGrabbed,
        bool controllerPoseValid,
        bool expected)
    {
        bool result = OpenVrInteractionGate.ShouldEndGrab(
            grabPressed,
            alreadyGrabbed,
            controllerPoseValid);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0.003f, 0.002f, false)]
    [InlineData(0.007f, 0, true)]
    public void TouchRequiresIntentionalTravelBeforeDragging(
        float deltaX,
        float deltaY,
        bool expected)
    {
        bool startsDrag = OpenVrTouchGestureGate.ShouldStartDrag(
            0.5f,
            0.5f,
            0.5f + deltaX,
            0.5f + deltaY);

        Assert.Equal(expected, startsDrag);
    }

    [Theory]
    [InlineData(0.002f, 8, false)]
    [InlineData(0.0005f, 16, false)]
    [InlineData(0.002f, 16, true)]
    public void TouchMoveRequiresTimeAndDistance(
        float deltaX,
        long elapsedMilliseconds,
        bool expected)
    {
        bool sendsMove = OpenVrTouchGestureGate.ShouldSendMove(
            0.5f,
            0.5f,
            0.5f + deltaX,
            0.5f,
            elapsedMilliseconds);

        Assert.Equal(expected, sendsMove);
    }
}
