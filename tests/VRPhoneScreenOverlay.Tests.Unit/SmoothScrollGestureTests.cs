using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class SmoothScrollGestureTests
{
    [Fact]
    public void ProducesContinuousTouchGestureInsteadOfWheelSteps()
    {
        SmoothScrollGesture gesture = new();
        Span<SmoothScrollCommand> commands = stackalloc SmoothScrollCommand[2];

        int started = gesture.Update(0.8f, true, 0.4f, 0.5f, 1000, commands);
        Assert.Equal(1, started);
        Assert.Equal(PhoneInputCommandKind.PointerDown, commands[0].Kind);

        int moved = gesture.Update(0.8f, true, 0.4f, 0.5f, 1012, commands);
        Assert.Equal(1, moved);
        Assert.Equal(PhoneInputCommandKind.PointerMove, commands[0].Kind);
        Assert.True(commands[0].NormalizedY > 0.5f);

        int stopped = gesture.Update(0, true, 0.4f, 0.5f, 1024, commands);
        Assert.Equal(1, stopped);
        Assert.Equal(PhoneInputCommandKind.PointerUp, commands[0].Kind);
        Assert.False(gesture.Active);
    }

    [Fact]
    public void EndsGestureWhenPointerCanNoLongerScroll()
    {
        SmoothScrollGesture gesture = new();
        Span<SmoothScrollCommand> commands = stackalloc SmoothScrollCommand[2];

        _ = gesture.Update(-1f, true, 0.6f, 0.5f, 2000, commands);
        int stopped = gesture.Update(-1f, false, 0.6f, 0.5f, 2012, commands);

        Assert.Equal(1, stopped);
        Assert.Equal(PhoneInputCommandKind.PointerUp, commands[0].Kind);
        Assert.False(gesture.Active);
    }

    [Fact]
    public void IgnoresSmallJoystickNoise()
    {
        SmoothScrollGesture gesture = new();
        Span<SmoothScrollCommand> commands = stackalloc SmoothScrollCommand[2];

        int count = gesture.Update(0.2f, true, 0.5f, 0.5f, 3000, commands);

        Assert.Equal(0, count);
        Assert.False(gesture.Active);
    }

    [Fact]
    public void KeepsMinimumSpeedAndBoostsFullJoystickTravel()
    {
        Assert.Equal(0.18f, SmoothScrollGesture.CalculateSpeed(0));
        Assert.Equal(1.8f, SmoothScrollGesture.CalculateSpeed(1), precision: 5);
        Assert.True(
            SmoothScrollGesture.CalculateSpeed(0.75f) >
            SmoothScrollGesture.CalculateSpeed(0.5f));
    }
}
