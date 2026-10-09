using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPhoneMenuLayoutTests
{
    [Theory]
    [InlineData(73, 401, (int)OpenVrMenuTarget.Digit1)]
    [InlineData(180, 401, (int)OpenVrMenuTarget.Digit2)]
    [InlineData(286, 401, (int)OpenVrMenuTarget.Digit3)]
    [InlineData(73, 463, (int)OpenVrMenuTarget.Digit4)]
    [InlineData(180, 463, (int)OpenVrMenuTarget.Digit5)]
    [InlineData(286, 463, (int)OpenVrMenuTarget.Digit6)]
    [InlineData(73, 525, (int)OpenVrMenuTarget.Digit7)]
    [InlineData(180, 525, (int)OpenVrMenuTarget.Digit8)]
    [InlineData(286, 525, (int)OpenVrMenuTarget.Digit9)]
    [InlineData(73, 587, (int)OpenVrMenuTarget.Backspace)]
    [InlineData(180, 587, (int)OpenVrMenuTarget.Digit0)]
    [InlineData(286, 587, (int)OpenVrMenuTarget.Confirm)]
    public void EveryKeyMatchesItsRenderedCell(float x, float y, int expected)
    {
        Assert.True(OpenVrPhoneMenuLayout.TryMapKeypad(x / 360, (y - 320) / 320, out var target));
        Assert.Equal((OpenVrMenuTarget)expected, target);
        _ = OpenVrPhoneMenuLayout.TryMap(x / 360, y / 640, false, out var sidebarTarget, out _);
        Assert.False(sidebarTarget is >= OpenVrMenuTarget.Digit0 and <= OpenVrMenuTarget.Confirm);
    }

    [Theory]
    [InlineData(24, 0)]
    [InlineData(180, 0.5f)]
    [InlineData(336, 1)]
    public void SliderEndpointsAndMiddleMatchTheDrawnTrack(float x, float expected)
    {
        Assert.True(OpenVrPhoneMenuLayout.TryMap(x / 360, 203f / 640, false, out var target, out float fraction));
        Assert.Equal(OpenVrMenuTarget.Opacity, target);
        Assert.Equal(expected, fraction, 5);
    }

    [Fact]
    public void BlankPanelAreasDoNotDismissButTransparentOutsideAreasDo()
    {
        Assert.True(OpenVrPhoneMenuLayout.TryMap(0.5f, 534f / 640, true, out var target, out _));
        Assert.Equal(OpenVrMenuTarget.KeypadToggle, target);
        Assert.False(OpenVrPhoneMenuLayout.TryMap(0.5f, 534f / 640, false, out _, out _));
        Assert.False(OpenVrPhoneMenuLayout.TryMap(float.NaN, 0.5f, true, out _, out _));
    }

    [Theory]
    [InlineData(264, (int)OpenVrMenuTarget.ScreenGuardToggle)]
    [InlineData(328, (int)OpenVrMenuTarget.PlayspaceToggle)]
    public void GuardPrecedesPlayspaceWithoutTheOptionalKeypad(int y, int expected)
    {
        Assert.True(OpenVrPhoneMenuLayout.TryMap(0.5f, y / 640f, false, out var target, out _));
        Assert.Equal((OpenVrMenuTarget)expected, target);
    }
}
