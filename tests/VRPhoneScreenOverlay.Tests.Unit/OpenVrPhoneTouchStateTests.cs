using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPhoneTouchStateTests
{
    [Fact]
    public void TriggerJitterRemainsATapAtTheOriginalCoordinate()
    {
        OpenVrPhoneTouchState state = new();

        Assert.True(state.TryUpdate(true, 0.5f, 0.5f, 1_000, out OpenVrPhoneInputCommand down));
        state.RecordTouchState(true);
        Assert.False(state.TryUpdate(true, 0.503f, 0.502f, 1_016, out _));
        state.RecordTouchState(true);
        Assert.True(state.TryUpdate(false, 0.503f, 0.502f, 1_032, out OpenVrPhoneInputCommand up));

        Assert.Equal(PhoneInputCommandKind.PointerDown, down.Kind);
        Assert.Equal(PhoneInputCommandKind.PointerUp, up.Kind);
        Assert.Equal(down.NormalizedX, up.NormalizedX);
        Assert.Equal(down.NormalizedY, up.NormalizedY);
        Assert.False(state.IsDown);
    }

    [Fact]
    public void IntentionalMovementTransitionsFromPressToDrag()
    {
        OpenVrPhoneTouchState state = new();

        Assert.True(state.TryUpdate(true, 0.5f, 0.5f, 2_000, out _));
        state.RecordTouchState(true);
        Assert.True(state.TryUpdate(true, 0.51f, 0.5f, 2_016, out OpenVrPhoneInputCommand move));
        state.RecordTouchState(true);
        Assert.True(state.TryUpdate(false, 0.52f, 0.5f, 2_032, out OpenVrPhoneInputCommand up));

        Assert.Equal(PhoneInputCommandKind.PointerMove, move.Kind);
        Assert.Equal(PhoneInputCommandKind.PointerUp, up.Kind);
        Assert.Equal(0.52f, up.NormalizedX);
        Assert.False(state.IsDown);
    }

    [Fact]
    public void LeavingPhoneReleasesAtTheLastSentCoordinate()
    {
        OpenVrPhoneTouchState state = new();

        Assert.True(state.TryUpdate(true, 0.4f, 0.6f, 3_000, out OpenVrPhoneInputCommand down));
        state.RecordTouchState(true);
        Assert.True(state.TryRelease(out OpenVrPhoneInputCommand up));

        Assert.Equal(PhoneInputCommandKind.PointerUp, up.Kind);
        Assert.Equal(down.NormalizedX, up.NormalizedX);
        Assert.Equal(down.NormalizedY, up.NormalizedY);
        Assert.False(state.IsDown);
    }

    [Fact]
    public void GrabCanCancelAnActiveTouch()
    {
        OpenVrPhoneTouchState state = new();

        Assert.True(state.TryUpdate(true, 0.2f, 0.3f, 4_000, out _));
        Assert.True(state.TryCancel(out OpenVrPhoneInputCommand cancel));

        Assert.Equal(PhoneInputCommandKind.PointerCancel, cancel.Kind);
        Assert.False(state.IsDown);
    }
}
