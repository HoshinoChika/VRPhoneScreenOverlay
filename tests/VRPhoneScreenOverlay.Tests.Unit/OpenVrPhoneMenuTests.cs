using Valve.VR;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using VRPhoneScreenOverlay.SteamVR;
using static Vortice.Direct3D11.D3D11;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPhoneMenuTests
{
    [Fact]
    public void BothResetArrowsSwitchBetweenTheTwoModes()
    {
        OpenVrPhoneMenuState state = Ready();
        Click(state, OpenVrMenuTarget.Dot);
        Click(state, OpenVrMenuTarget.MotionSettings);
        Assert.True(Click(state, OpenVrMenuTarget.ResetModePrevious).Playspace?.MotionOptions?.ResetAllOffsets);
        Assert.False(Click(state, OpenVrMenuTarget.ResetMode).Playspace?.MotionOptions?.ResetAllOffsets);
    }

    [Fact]
    public void OpenVrSettingsPagesObserveDesktopChangesAndKeepUnrelatedVideoDraftFields()
    {
        OpenVrVideoSettingsChannel channel = new();
        channel.ConfigureChoices([16, 24, 32], [60, 90, 120]);
        OpenVrPhoneMenuState state = Ready();
        state.VideoSettings = channel;
        Click(state, OpenVrMenuTarget.Dot);
        Click(state, OpenVrMenuTarget.VideoSettings);
        Click(state, OpenVrMenuTarget.FrameRateUp);
        channel.Publish(new(100, 24, 60), [100]);
        state.SynchronizeSettings(_playspace);
        Assert.Equal(new OpenVrVideoOptions(100, 24, 90), state.VideoDraft);
        Click(state, OpenVrMenuTarget.VideoBack);
        Click(state, OpenVrMenuTarget.MotionSettings);
        OpenVrPlayspaceMotionOptions motion = OpenVrPlayspaceMotionOptions.Default with { FlingStrength = 3 };
        state.SynchronizeSettings(_playspace with { MotionOptions = motion });
        Assert.Equal(motion, state.MotionDraft);
    }

    private static readonly OpenVrSharedPlayspaceInput _playspace = new(true, false, false, false, 5);

    [Fact]
    public void HoldingMotionPlusRepeatsButDashboardRequiresAReleaseAgain()
    {
        OpenVrPhoneMenuState state = Ready();
        Click(state, OpenVrMenuTarget.Dot);
        Click(state, OpenVrMenuTarget.MotionSettings);
        Release(state);
        state.ProcessInput(true, true, OpenVrMenuTarget.StrengthUp, 0, true, _playspace, 0);
        Assert.Equal(1.1f, state.MotionDraft.FlingStrength);
        Assert.Null(state.ProcessInput(true, true, OpenVrMenuTarget.StrengthUp, 0, true, _playspace, 399).Playspace);
        Assert.NotNull(state.ProcessInput(true, true, OpenVrMenuTarget.StrengthUp, 0, true, _playspace, 400).Playspace);
        Assert.Equal(1.2f, state.MotionDraft.FlingStrength);
        state.ProcessInput(true, true, OpenVrMenuTarget.StrengthUp, 0, true, _playspace, 2000);
        Assert.Equal(1.7f, state.MotionDraft.FlingStrength);
        state.SetEnvironment(true, true, true);
        state.SetEnvironment(true, false, true);
        Assert.Null(state.ProcessInput(true, true, OpenVrMenuTarget.StrengthUp, 0, true, _playspace, 3000).Playspace);
        Assert.Equal(1.7f, state.MotionDraft.FlingStrength);
    }

    [Fact]
    public void GuardUsesTheInputGateAndWaitsForSessionStateBeforeChangingItsVisual()
    {
        OpenVrPhoneMenuState state = Ready();
        Click(state, OpenVrMenuTarget.Dot);
        Assert.Equal(VRPhoneScreenOverlay.Contracts.PhoneInputCommandKind.ToggleScreenGuard,
            Click(state, OpenVrMenuTarget.ScreenGuardToggle).PhoneCommand?.Kind);
        Assert.False(state.ScreenGuardEnabled);
        state.SetScreenGuard(true, false);
        Assert.True(state.ScreenGuardEnabled);
        Assert.Equal(VRPhoneScreenOverlay.Contracts.PhoneInputCommandKind.WakeScreen,
            Click(state, OpenVrMenuTarget.KeypadToggle).PhoneCommand?.Kind);
        state.SetEnvironment(true, true, false);
        Assert.Null(Click(state, OpenVrMenuTarget.ScreenGuardToggle).PhoneCommand);
        Assert.Null(Click(state, OpenVrMenuTarget.KeypadToggle).PhoneCommand);
    }

    [Fact]
    public void HideFoldsMenuAndRestorePreservesUserIntentAcrossDashboard()
    {
        OpenVrPhoneMenuState state = Ready();
        Click(state, OpenVrMenuTarget.Dot);
        Assert.True(state.SidebarVisible);
        Assert.True(Click(state, OpenVrMenuTarget.PhoneToggle).VisibilityChanged);
        Assert.True(state.PhoneHidden);
        Assert.False(state.Expanded);
        Assert.False(state.PhoneVisible);
        Assert.False(state.ControlsVisible);
        Click(state, OpenVrMenuTarget.Dot);
        Assert.False(state.SidebarVisible);
        Assert.False(state.ProcessRecall(true)); // A held key cannot recall.
        Assert.False(state.ProcessRecall(false));
        Assert.True(state.ProcessRecall(true));
        int beforeDashboard = state.InputRevision;
        for (int frame = 0; frame < 50; frame++)
        {
            state.SetEnvironment(true, true, true);
            Assert.False(state.PhoneVisible || state.SidebarVisible || state.ControlsVisible);
            Assert.False(state.PhoneHidden || state.Expanded);
        }
        Assert.Equal(beforeDashboard + 1, state.InputRevision);
        state.SetEnvironment(true, false, true);
        Assert.False(state.SidebarVisible || state.KeypadVisible);
        Assert.True(state.PhoneVisible);
        Assert.True(state.SuppressPhoneTouch);
        Click(state, OpenVrMenuTarget.Dot);
        Assert.True(state.PhoneVisible && state.SidebarVisible);
        state.SetEnvironment(true, true, true);
        state.SetEnvironment(true, false, true);
        Assert.True(state.PhoneVisible && state.SidebarVisible);
    }

    [Fact]
    public void OutsideClickDismissesWithoutLeakingItsHeldPressIntoPhoneOrKeypad()
    {
        OpenVrPhoneMenuState state = Ready();
        Click(state, OpenVrMenuTarget.Dot);
        Release(state);
        OpenVrMenuChange dismissed = state.ProcessInput(true, false, OpenVrMenuTarget.None, 0, true, _playspace);
        Assert.True(dismissed.Consumed);
        Assert.False(state.Expanded);
        Assert.True(state.SuppressPhoneTouch);
        Assert.Null(state.ProcessInput(true, true, OpenVrMenuTarget.Dot, 0, true, _playspace).PhoneCommand);
        Assert.False(state.Expanded);
        Release(state);
        Assert.False(state.SuppressPhoneTouch);
    }

    [Fact]
    public void SliderKeepsFocusAndDashboardRequiresAReleaseBeforeNextClick()
    {
        OpenVrPhoneMenuState state = Ready();
        Click(state, OpenVrMenuTarget.Dot);
        Release(state);
        Assert.True(state.ProcessInput(true, true, OpenVrMenuTarget.Opacity, 0.5f, true, _playspace).OpacityChanged);
        Assert.Equal(50, state.OpacityPercent);
        state.ProcessInput(true, true, OpenVrMenuTarget.Opacity, 0, true, _playspace);
        Assert.Equal(0, state.OpacityPercent);
        state.ProcessInput(true, false, OpenVrMenuTarget.None, 1, true, _playspace);
        Assert.True(state.Expanded);
        Assert.Equal(0, state.OpacityPercent);
        state.SetEnvironment(true, true, true);
        state.SetEnvironment(true, false, true);
        state.ProcessInput(true, true, OpenVrMenuTarget.PhoneToggle, 0, true, _playspace);
        Assert.False(state.PhoneHidden);
        Assert.True(Click(state, OpenVrMenuTarget.PhoneToggle).VisibilityChanged);
    }

    [Fact]
    public void KeypadOnlyExistsWhenConfiguredAndNeverStoresTheEnteredPin()
    {
        OpenVrPhoneMenuState state = Ready();
        Click(state, OpenVrMenuTarget.Dot);
        Click(state, OpenVrMenuTarget.KeypadToggle);
        Click(state, OpenVrMenuTarget.Dot); // Keys remain independent of the sidebar.
        OpenVrMenuChange digit = Click(state, OpenVrMenuTarget.Digit7);
        Assert.Equal(7, digit.PhoneCommand!.Value.UnlockDigit);
        Assert.Equal(1, state.EnteredDigitCount);
        Click(state, OpenVrMenuTarget.Backspace);
        Assert.Equal(0, state.EnteredDigitCount);
        state.SetEnvironment(true, false, false);
        Assert.False(state.KeypadVisible);
        Assert.Null(Click(state, OpenVrMenuTarget.Digit7).PhoneCommand);
    }

    [Fact]
    public void DotAndPanelStayOutsidePhoneAndHiddenDotUsesCurrentHeadPose()
    {
        HmdMatrix34_t phone = OpenVrTransformMath.Identity();
        phone.m11 = -1;
        HmdMatrix34_t head = OpenVrTransformMath.Identity();
        HmdMatrix34_t dot = OpenVrPhoneMenuLayout.DotTransform(false, phone, 0.6f, 0.5f, head);
        Assert.Equal(0.3152f, dot.m3 - OpenVrPhoneMenuLayout.DotWidth / 2, 5);
        Assert.Equal(0.6072f, dot.m7 + OpenVrPhoneMenuLayout.DotWidth / 2, 5);
        HmdMatrix34_t panel = OpenVrPhoneMenuLayout.PanelTransform(dot);
        Assert.True(panel.m3 - OpenVrPhoneMenuLayout.PanelWidth / 2 > dot.m3);
        head.m3 = 2;
        HmdMatrix34_t hiddenDot = OpenVrPhoneMenuLayout.DotTransform(true, phone, 0.6f, 0.5f, head);
        Assert.Equal(2, hiddenDot.m3);
        Assert.Equal(0.26f, hiddenDot.m7, 4);
        Assert.Equal(dot, OpenVrPhoneMenuLayout.DotTransform(false, phone, 0.6f, 0.5f, head));
        Assert.True(OpenVrPhoneMenuLayout.InsideDot(0, 0));
        Assert.False(OpenVrPhoneMenuLayout.InsideDot(-0.001f, 0));
        Assert.True(OpenVrPhoneMenuLayout.InsideDot(0.5f, 0.5f));
        byte[] pixels = OpenVrPhoneMenuPixels.Dot();
        Assert.Equal(0, pixels[3]);
        Assert.Equal(255, pixels[(32 * 64 + 32) * 4 + 3]);
    }

    [Fact]
    public void FirstVisibleFrameAndDashboardReturnPresentWithoutAValueChangingClick()
    {
        using OpenVrControlTextureTests.OverlayBoundary boundary = new();
        using OpenVrPhoneMenuView view = new(boundary.Api, (width, height) => new D3D11ControlTexture(
            D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0), width, height));
        OpenVrPhoneMenuState state = Ready();
        HmdMatrix34_t phone = OpenVrTransformMath.Identity();
        phone.m11 = -1;
        HmdMatrix34_t head = OpenVrTransformMath.Identity();
        view.Render(state, phone, 0.6f, 0.5f, head, _playspace);
        Assert.Single(boundary.Visible);
        Assert.Equal(3, boundary.Presented.Count); // Folded surfaces are already uploaded.
        Click(state, OpenVrMenuTarget.Dot);
        view.Render(state, phone, 0.6f, 0.5f, head, _playspace);
        Assert.Equal(2, boundary.Visible.Count);
        Assert.Contains(2UL, boundary.Presented);
        state.SetEnvironment(true, true, true);
        view.Render(state, phone, 0.6f, 0.5f, head, _playspace);
        Assert.Empty(boundary.Visible);
        int submissions = boundary.TextureSubmissions;
        state.SetEnvironment(true, false, true);
        view.Render(state, phone, 0.6f, 0.5f, head, _playspace);
        Assert.Equal(2, boundary.Visible.Count);
        Assert.Equal(submissions, boundary.TextureSubmissions); // Stable UI needs no resubmit.
        Assert.Equal(0, boundary.RawBlocks);
    }

    [Fact]
    public void DashboardAlsoBlocksPlayspaceUntilBothButtonsAreReleased()
    {
        OpenVrDashboardInputGate gate = new();
        gate.Apply(true, new(true, true, true, true), true, out bool drag, out bool reset);
        Assert.False(drag || reset);
        gate.Apply(false, new(false, true, false, true), true, out drag, out reset);
        Assert.False(drag);
        gate.Apply(false, new(false, false, false, true), true, out drag, out reset);
        gate.Apply(false, new(false, true, false, true), true, out drag, out reset);
        Assert.True(drag);
    }

    [Fact]
    public void LockTransitionsOpenTheIndependentKeypadButDoNotUndoManualDismissal()
    {
        OpenVrPhoneMenuState state = Ready();
        state.SetLocked(true);
        Assert.True(state.KeypadVisible);
        Assert.False(state.SidebarVisible);
        Click(state, OpenVrMenuTarget.KeypadToggle);
        state.SetLocked(true);
        Assert.False(state.KeypadVisible);
        state.SetLocked(false);
        state.SetLocked(true);
        Assert.True(state.KeypadVisible);
        state.SetEnvironment(true, true, true);
        Assert.False(state.KeypadVisible);
        state.SetEnvironment(true, false, true);
        Assert.True(state.KeypadVisible);
        state.SetLocked(null);
        Assert.True(state.KeypadVisible); // An unknown probe cannot close it mid-PIN.
        state.SetLocked(false);
        Assert.False(state.KeypadVisible);
        Assert.False(state.IsSurfaceVisible(OpenVrPhoneSurface.Keypad));
        Assert.True(state.IsSurfaceVisible(OpenVrPhoneSurface.Phone));
    }

    [Fact]
    public void RecallWhileDashboardIsOpenOrKeyIsHeldCannotRestoreAnySurface()
    {
        OpenVrPhoneMenuState state = Ready();
        Click(state, OpenVrMenuTarget.Dot);
        Click(state, OpenVrMenuTarget.PhoneToggle);
        state.SetEnvironment(true, true, true);
        state.ProcessRecall(false);
        Assert.False(state.ProcessRecall(true));
        state.SetEnvironment(true, false, true);
        Assert.False(state.ProcessRecall(true));
        Assert.False(state.ControlsVisible);
        state.ProcessRecall(false);
        Assert.True(state.ProcessRecall(true));
        Assert.True(state.PhoneVisible);
        Assert.False(state.SidebarVisible || state.KeypadVisible);
    }

    [Fact]
    public void RecallRestoresPhoneImmediatelyWithoutReopeningKeypadOrPassingHeldTouch()
    {
        OpenVrPhoneMenuState state = Ready();
        state.SetLocked(true);
        Click(state, OpenVrMenuTarget.Dot);
        Click(state, OpenVrMenuTarget.PhoneToggle);
        state.ProcessRecall(false);
        Assert.True(state.ProcessRecall(true));
        Assert.True(state.PhoneVisible && state.ControlsVisible);
        Assert.False(state.SidebarVisible || state.KeypadVisible);
        Assert.True(state.SuppressPhoneTouch);
        Assert.False(state.ProcessRecall(true));
        Assert.False(state.ProcessRecall(false));
        Assert.False(state.ProcessRecall(true));
        state.SetEnvironment(true, true, true);
        state.SetEnvironment(true, false, true);
        Assert.True(state.PhoneVisible);
        Release(state);
        Assert.False(state.SuppressPhoneTouch);
    }

    [Fact]
    public void MotionChangesApplyImmediatelyWithoutSaveButton()
    {
        OpenVrPhoneMenuState state = Ready();
        Click(state, OpenVrMenuTarget.Dot);
        var toggle = Click(state, OpenVrMenuTarget.FlingToggle).Playspace;
        Assert.True(toggle!.FlingEnabled);
        Assert.Null(toggle.MotionOptions);
        Click(state, OpenVrMenuTarget.MotionSettings);
        Assert.True(state.MotionExpanded);
        Assert.Equal(10, Click(state, OpenVrMenuTarget.MultiplierUp).Playspace!.Multiplier);
        Assert.NotNull(Click(state, OpenVrMenuTarget.StrengthUp).Playspace);
        Assert.Equal(1.1f, state.MotionDraft.FlingStrength);
        Assert.NotNull(Click(state, OpenVrMenuTarget.GravityDown).Playspace);
        Assert.Equal(9.7f, state.MotionDraft.Gravity);
        Click(state, OpenVrMenuTarget.FrictionDown);
        Assert.Equal(0, state.MotionDraft.Friction);
        Click(state, OpenVrMenuTarget.ResetMode);
        Assert.True(state.MotionDraft.ResetAllOffsets);
        Click(state, OpenVrMenuTarget.MotionBack);
        Assert.False(state.MotionExpanded);
    }

    private static OpenVrPhoneMenuState Ready()
    {
        OpenVrPhoneMenuState state = new();
        state.SetEnvironment(true, false, true);
        return state;
    }
    private static void Release(OpenVrPhoneMenuState state) => state.ProcessInput(true, false, OpenVrMenuTarget.None, 0, false, _playspace);
    private static OpenVrMenuChange Click(OpenVrPhoneMenuState state, OpenVrMenuTarget target)
    {
        Release(state);
        return state.ProcessInput(true, true, target, 0, true, _playspace);
    }
}
