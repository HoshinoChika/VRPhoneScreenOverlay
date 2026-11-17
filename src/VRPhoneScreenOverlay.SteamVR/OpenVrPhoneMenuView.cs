using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

// Display/update is independent of hit testing. All three GPU surfaces are
// prepared before the first phone presentation, even while their UI is folded.
internal sealed class OpenVrPhoneMenuView(CVROverlay overlay, Func<int, int, D3D11ControlTexture>? textureFactory = null) : IDisposable
{
    private readonly OpenVrScreenshotView _screenshot = new(overlay, textureFactory);
    private readonly OpenVrClipboardView _clipboard = new(overlay, textureFactory);
    private readonly OpenVrStaticSurface _placeholder = new(overlay, "io.github.vrphonescreen.overlay.phone.disconnected", textureFactory);
    private OpenVrPhonePlaceholderState? _placeholderUploaded;
    private readonly OpenVrStaticSurface _dot = new(overlay, "io.github.vrphonescreen.overlay.phone.menu-dot", textureFactory);
    private readonly OpenVrStaticSurface _panel = new(overlay, "io.github.vrphonescreen.overlay.phone.menu-panel", textureFactory);
    private readonly OpenVrStaticSurface _keypad = new(overlay, "io.github.vrphonescreen.overlay.phone.unlock-keypad", textureFactory);
    private readonly OpenVrStaticSurface _translationButton = new(overlay, "io.github.vrphonescreen.overlay.phone.translation-button", textureFactory);
    private readonly OpenVrStaticSurface _translationPanel = new(overlay, "io.github.vrphonescreen.overlay.phone.translation-panel", textureFactory);
    private HmdMatrix34_t _translationButtonTransform;
    private HmdMatrix34_t _translationPanelTransform;
    private bool _translationButtonVisible;
    private bool _translationVisible;
    private bool _translationButtonUploaded;
    private TranslationPanelFrame? _translationFrame;
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
        state.SynchronizePhoneConnection();
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
        if (state.DisconnectedPlaceholder && state.PhoneVisible)
        {
            if (_placeholderUploaded != state.PlaceholderState && _placeholder.CanUpdatePixels)
            {
                if (_placeholder.SetPixels(OpenVrDisconnectedPhonePixels.Create(state.PlaceholderState),
                    OpenVrDisconnectedPhonePixels.Size, OpenVrDisconnectedPhonePixels.Size))
                { _placeholderUploaded = state.PlaceholderState; }
            }
            _placeholder.Show(phone, width, aspect, state.OpacityPercent / 100f);
        }
        else { _placeholder.Hide(); }
        HmdMatrix34_t root = state.PhoneHidden ? head : phone;
        _clipboard.Render(state, root, width, aspect);
        _screenshot.Render(state, root, width, aspect);
        _dotTransform = OpenVrPhoneGroupLayout.SurfacePose(OpenVrPhoneSurface.Dot, root, width, aspect, state.PhoneHidden);
        _dotVisible = _dot.Show(_dotTransform, OpenVrPhoneMenuLayout.DotWidth);
        if (state.Translation is not null)
        {
            if (!_translationButtonUploaded && _translationButton.CanUpdatePixels)
            { _translationButtonUploaded = _translationButton.SetPixels(TranslationButtonPixels(), 64, 64); }
            _translationButtonTransform = OpenVrPhoneGroupLayout.SurfacePose(OpenVrPhoneSurface.TranslationButton, root, width, aspect, state.PhoneHidden);
            _translationButtonVisible = _translationButton.Show(_translationButtonTransform, OpenVrPhoneMenuLayout.DotWidth);
            TranslationPanelFrame? frame = state.Translation.Frame;
            if (frame is not null && frame != _translationFrame && _translationPanel.CanUpdatePixels &&
                _translationPanel.SetPixels(frame.Pixels, OpenVrTranslationChannel.Width, OpenVrTranslationChannel.Height))
            { _translationFrame = frame; }
            _translationPanelTransform = OpenVrPhoneGroupLayout.SurfacePose(OpenVrPhoneSurface.TranslationPanel, root, width, aspect, state.PhoneHidden);
            _translationVisible = state.TranslationVisible && _translationPanel.Show(_translationPanelTransform, OpenVrTranslationChannel.WidthMeters);
            if (!state.TranslationVisible) { _translationPanel.Hide(); }
        }
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
        if (_screenshot.TryPick(pointer, out hit)) { return true; }
        if (_clipboard.TryPick(pointer, out hit)) { return true; }
        if (_translationButtonVisible && OpenVrPhoneMenuLayout.TryIntersect(pointer, _translationButtonTransform,
            OpenVrPhoneMenuLayout.DotWidth, 1, out var translationButtonHit))
        {
            hit = new(OpenVrMenuTarget.TranslationToggle, 0, translationButtonHit, OpenVrPhoneSurface.TranslationButton);
            return true;
        }
        if (_translationVisible && OpenVrPhoneMenuLayout.TryIntersect(pointer, _translationPanelTransform,
            OpenVrTranslationChannel.WidthMeters, OpenVrTranslationChannel.Aspect, out var translationHit))
        {
            int x = (int)(translationHit.vUVs.v0 * OpenVrTranslationChannel.Width);
            int y = (int)((1 - translationHit.vUVs.v1) * OpenVrTranslationChannel.Height);
            if (!VRPhoneScreenOverlay.Presentation.RoundedPanelGeometry.Contains(x, y, OpenVrTranslationChannel.Width, OpenVrTranslationChannel.Height)) { return false; }
            int index = -1;
            if (_translationFrame is not null)
            {
                for (int i = 0; i < _translationFrame.Hits.Count; i++)
                {
                    TranslationPanelHit area = _translationFrame.Hits[i];
                    if (x >= area.X && x < area.X + area.Width && y >= area.Y && y < area.Y + area.Height) { index = i; break; }
                }
            }
            hit = new(OpenVrMenuTarget.TranslationAction, index, translationHit, OpenVrPhoneSurface.TranslationPanel,
                index >= 0 ? _translationFrame!.Hits[index] : null);
            return true;
        }
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
        _screenshot.Hide();
        _placeholder.Hide();
        _clipboard.Hide();
        _dot.Hide();
        _panel.Hide();
        _keypad.Hide();
        _translationButton.Hide();
        _translationPanel.Hide();
        _translationButtonVisible = false;
        _translationVisible = false;
        _keypadVisible = false;
        _dotVisible = false;
        _panelVisible = false;
    }

    private static byte[] TranslationButtonPixels()
    {
        byte[] pixels = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                int offset = (y * 64 + x) * 4;
                bool glyph = y is >= 14 and <= 49 && (Math.Abs(x - (32 - (y - 14) * 0.4)) < 2 ||
                    Math.Abs(x - (32 + (y - 14) * 0.4)) < 2 || y is >= 36 and <= 39 && x is >= 22 and <= 42);
                float qx = MathF.Abs(x - 31.5f) - 20;
                float qy = MathF.Abs(y - 31.5f) - 20;
                float edge = MathF.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) +
                    Math.Min(Math.Max(qx, qy), 0) - 12;
                float rim = Math.Clamp(1 + edge, 0, 1);
                pixels[offset] = glyph ? (byte)245 : (byte)(22 + 50 * rim);
                pixels[offset + 1] = glyph ? (byte)245 : (byte)(26 + 55 * rim);
                pixels[offset + 2] = glyph ? (byte)245 : (byte)(33 + 60 * rim);
                pixels[offset + 3] = (byte)(Math.Clamp(0.5f - edge, 0, 1) * 255);
            }
        }
        return pixels;
    }

    public void Dispose() { _screenshot.Dispose(); _placeholder.Dispose(); _clipboard.Dispose(); _dot.Dispose(); _panel.Dispose(); _keypad.Dispose(); _translationButton.Dispose(); _translationPanel.Dispose(); }
}
