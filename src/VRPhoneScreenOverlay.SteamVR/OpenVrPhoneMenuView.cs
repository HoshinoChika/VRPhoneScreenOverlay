using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

// Display/update is independent of hit testing. All three GPU surfaces are
// prepared before the first phone presentation, even while their UI is folded.
internal sealed class OpenVrPhoneMenuView(CVROverlay overlay, Func<int, int, D3D11ControlTexture>? textureFactory = null) : IDisposable
{
    private readonly OpenVrStaticSurface _dot = new(overlay, "io.github.vrphonescreen.overlay.phone.menu-dot", textureFactory);
    private readonly OpenVrStaticSurface _panel = new(overlay, "io.github.vrphonescreen.overlay.phone.menu-panel", textureFactory);
    private readonly OpenVrStaticSurface _keypad = new(overlay, "io.github.vrphonescreen.overlay.phone.unlock-keypad", textureFactory);
    private readonly byte[] _keypadPixels = new byte[OpenVrPhoneMenuLayout.Width * OpenVrPhoneMenuLayout.KeypadHeight * 4];
    private int _paintedDigits = -1;
    private bool _keypadVisible;
    private HmdMatrix34_t _keypadTransform;
    private bool _dotUploaded;
    private bool _dotVisible;
    private bool _panelVisible;
    private bool _motionVisible;
    private bool _videoVisible;
    private float _adjacentHeight;
    private HmdMatrix34_t _dotTransform;
    private HmdMatrix34_t _panelTransform;
    private OpenVrMenuVisual? _painted;
    private readonly byte[] _pixels = new byte[OpenVrPhoneMenuLayout.Width * OpenVrPhoneMenuLayout.Height * 4];

    public void Render(OpenVrPhoneMenuState state, HmdMatrix34_t phone, float width, float aspect,
        HmdMatrix34_t head, OpenVrSharedPlayspaceInput playspace)
    {
        if (!_dotUploaded && _dot.CanUpdatePixels) { _dotUploaded = _dot.SetPixels(OpenVrPhoneMenuPixels.Dot(), 64, 64); }
        OpenVrMenuVisual visual = new(state.PhoneHidden, state.OpacityPercent, playspace.PlayspaceEnabled,
            playspace.Multiplier, state.KeypadEnabled, 0, state.KeypadVisible, state.ScreenGuardEnabled, state.ScreenGuardFaulted, state.MotionExpanded, playspace.FlingEnabled, state.MotionDraft,
            playspace.MotionSaveState == OpenVrMotionSaveState.Saved && state.MotionDraft != playspace.MotionOptions
                ? OpenVrMotionSaveState.None : playspace.MotionSaveState, state.VideoExpanded, state.VideoDraft,
            state.VideoSettings?.State ?? OpenVrVideoApplyState.Idle);
        _motionVisible = state.MotionExpanded;
        _videoVisible = state.VideoExpanded;
        if (_painted != visual && _panel.CanUpdatePixels)
        {
            OpenVrPhoneMenuPixels.PaintPanel(_pixels, visual);
            if (_panel.SetPixels(_pixels, OpenVrPhoneMenuLayout.Width, OpenVrPhoneMenuLayout.Height)) { _painted = visual; }
        }
        if (_paintedDigits != state.EnteredDigitCount && _keypad.CanUpdatePixels)
        {
            OpenVrPhoneMenuPixels.PaintKeypad(_keypadPixels, state.EnteredDigitCount);
            if (_keypad.SetPixels(_keypadPixels, OpenVrPhoneMenuLayout.Width, OpenVrPhoneMenuLayout.KeypadHeight))
            { _paintedDigits = state.EnteredDigitCount; }
        }
        if (!state.ControlsVisible) { Hide(); return; }
        HmdMatrix34_t root = state.PhoneHidden ? head : phone;
        _dotTransform = OpenVrPhoneGroupLayout.SurfacePose(OpenVrPhoneSurface.Dot, root, width, aspect, state.PhoneHidden);
        _dotVisible = _dot.Show(_dotTransform, OpenVrPhoneMenuLayout.DotWidth);
        var shell = OpenVrPhoneShellGeometry.OuterSize(width, aspect);
        _adjacentHeight = Math.Max(shell.Width / shell.Aspect,
            OpenVrPhoneMenuLayout.PanelWidth * (state.MotionExpanded || state.VideoExpanded ? OpenVrPhoneMenuLayout.MotionHeight : state.KeypadEnabled ? 564 : 500) / OpenVrPhoneMenuLayout.Width);
        _panelTransform = OpenVrPhoneGroupLayout.SurfacePose(OpenVrPhoneSurface.Menu, root, width, aspect, state.PhoneHidden);
        _panelVisible = state.SidebarVisible && _panel.Show(_panelTransform, OpenVrPhoneMenuLayout.PanelWidth);
        if (!state.SidebarVisible) { _panel.Hide(); }
        _keypadTransform = OpenVrPhoneGroupLayout.SurfacePose(OpenVrPhoneSurface.Keypad, phone, width, aspect, false);
        _keypadVisible = state.KeypadVisible && _keypad.Show(_keypadTransform, OpenVrPhoneMenuLayout.KeypadWidth);
        if (!state.KeypadVisible) { _keypad.Hide(); }
    }

    public bool TryPick(HmdMatrix34_t pointer, bool keypad, out OpenVrPhoneMenuHit hit, bool opacityCaptured = false)
    {
        hit = default;
        if (opacityCaptured && _panelVisible && !_motionVisible && !_videoVisible &&
            OpenVrPhoneMenuLayout.TryIntersectOpacityDrag(pointer, _panelTransform, out var dragHit, out float dragFraction))
        {
            hit = new(OpenVrMenuTarget.Opacity, dragFraction, dragHit, OpenVrPhoneSurface.Menu);
            return true;
        }
        if (_keypadVisible && OpenVrPhoneMenuLayout.TryIntersect(pointer, _keypadTransform,
            OpenVrPhoneMenuLayout.KeypadWidth, OpenVrPhoneMenuLayout.KeypadAspect, out var keypadHit) &&
            OpenVrPhoneMenuLayout.TryMapKeypad(keypadHit.vUVs.v0, 1 - keypadHit.vUVs.v1, out var key))
        {
            hit = new(key, 0, keypadHit, OpenVrPhoneSurface.Keypad);
            return true;
        }
        if (_dotVisible && OpenVrPhoneMenuLayout.TryIntersectGearRegion(pointer, _dotTransform,
            _panelVisible, _adjacentHeight, out var dotHit))
        {
            hit = new(OpenVrMenuTarget.Dot, 0, dotHit, OpenVrPhoneSurface.Dot);
            return true;
        }
        if (_panelVisible && OpenVrPhoneMenuLayout.TryIntersect(pointer, _panelTransform, OpenVrPhoneMenuLayout.PanelWidth,
            OpenVrPhoneMenuLayout.PanelAspect, out var panelHit) &&
            OpenVrPhoneMenuLayout.TryMap(panelHit.vUVs.v0, 1 - panelHit.vUVs.v1, keypad, out var target, out float fraction, _motionVisible, _videoVisible))
        {
            hit = new(target, fraction, panelHit, OpenVrPhoneSurface.Menu);
            return true;
        }
        return false;
    }

    public void Hide()
    {
        _dot.Hide();
        _panel.Hide();
        _keypad.Hide();
        _keypadVisible = false;
        _dotVisible = false;
        _panelVisible = false;
    }

    public void Dispose() { _dot.Dispose(); _panel.Dispose(); _keypad.Dispose(); }
}
