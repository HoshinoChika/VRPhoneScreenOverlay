using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrClipboardView(CVROverlay overlay, Func<int, int, D3D11ControlTexture>? textureFactory = null) : IDisposable
{
    private readonly OpenVrStaticSurface _button = new(overlay, "io.github.vrphonescreen.overlay.phone.clipboard-button", textureFactory);
    private readonly OpenVrStaticSurface _panel = new(overlay, "io.github.vrphonescreen.overlay.phone.clipboard-panel", textureFactory);
    private HmdMatrix34_t _buttonPose;
    private HmdMatrix34_t _panelPose;
    private bool _buttonUploaded;
    private bool _paintedEnabled;
    private bool _buttonVisible;
    private bool _panelVisible;
    private ClipboardPanelFrame? _painted;
    public void Render(OpenVrPhoneMenuState state, HmdMatrix34_t root, float width, float aspect)
    {
        if (!state.ControlsVisible || state.Clipboard is null) { Hide(); return; }
        if ((!_buttonUploaded || _paintedEnabled != state.PhoneConnected) && _button.CanUpdatePixels)
        {
            bool enabled = state.PhoneConnected;
            _buttonUploaded = _button.SetPixels(enabled ? ButtonPixels() : OpenVrDisabledIcon.Create(ButtonPixels()), 64, 64);
            if (_buttonUploaded) { _paintedEnabled = enabled; }
        }
        _buttonPose = OpenVrPhoneGroupLayout.SurfacePose(OpenVrPhoneSurface.ClipboardButton, root, width, aspect, false);
        _buttonVisible = _button.Show(_buttonPose, OpenVrPhoneMenuLayout.DotWidth);
        ClipboardPanelFrame? next = state.Clipboard.Frame;
        if (next is not null && next != _painted && _panel.CanUpdatePixels &&
            _panel.SetPixels(next.Pixels, OpenVrTranslationChannel.Width, OpenVrTranslationChannel.Height)) { _painted = next; }
        _panelPose = OpenVrPhoneGroupLayout.SurfacePose(OpenVrPhoneSurface.ClipboardPanel, root, width, aspect, false);
        _panelVisible = state.ClipboardVisible && _panel.Show(_panelPose, OpenVrTranslationChannel.WidthMeters);
        if (!state.ClipboardVisible) { _panel.Hide(); }
    }
    public bool TryPick(HmdMatrix34_t pointer, out OpenVrPhoneMenuHit hit)
    {
        hit = default;
        if (_buttonVisible && OpenVrPhoneMenuLayout.TryIntersect(pointer, _buttonPose, OpenVrPhoneMenuLayout.DotWidth, 1, out var button))
        { hit = new(OpenVrMenuTarget.ClipboardToggle, 0, button, OpenVrPhoneSurface.ClipboardButton); return true; }
        if (_panelVisible && OpenVrPhoneMenuLayout.TryIntersect(pointer, _panelPose,
            OpenVrTranslationChannel.WidthMeters, OpenVrTranslationChannel.Aspect, out var panel))
        {
            int x = (int)(panel.vUVs.v0 * OpenVrTranslationChannel.Width);
            int y = (int)((1 - panel.vUVs.v1) * OpenVrTranslationChannel.Height);
            if (!VRPhoneScreenOverlay.Presentation.RoundedPanelGeometry.Contains(x, y, OpenVrTranslationChannel.Width, OpenVrTranslationChannel.Height)) { return false; }
            ClipboardPanelHit? selected = _painted?.Hits.FirstOrDefault(area => x >= area.X && x < area.X + area.Width && y >= area.Y && y < area.Y + area.Height);
            hit = new(OpenVrMenuTarget.ClipboardAction, 0, panel, OpenVrPhoneSurface.ClipboardPanel, ClipboardHit: selected);
            return true;
        }
        return false;
    }
    internal static byte[] ButtonPixels()
    {
        byte[] pixels = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                int index = (y * 64 + x) * 4;
                float qx = MathF.Abs(x - 31.5f) - 20, qy = MathF.Abs(y - 31.5f) - 20;
                float edge = MathF.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - 12;
                bool icon = (x is >= 17 and <= 46 && y is >= 17 and <= 50 && (x < 20 || x > 43 || y > 47)) ||
                    (x is >= 24 and <= 39 && y is >= 12 and <= 21 && (x < 27 || x > 36 || y < 15 || y > 18)) ||
                    (x is >= 25 and <= 38 && y is 29 or 30 or 37 or 38);
                float rim = Math.Clamp(1 + edge, 0, 1);
                pixels[index] = icon ? (byte)245 : (byte)(22 + 50 * rim);
                pixels[index + 1] = icon ? (byte)245 : (byte)(26 + 55 * rim);
                pixels[index + 2] = icon ? (byte)245 : (byte)(33 + 60 * rim);
                pixels[index + 3] = (byte)(Math.Clamp(0.5f - edge, 0, 1) * 255);
            }
        }
        return pixels;
    }
    public void Hide() { _button.Hide(); _panel.Hide(); _buttonVisible = false; _panelVisible = false; }
    public void Dispose() { _button.Dispose(); _panel.Dispose(); }
}
