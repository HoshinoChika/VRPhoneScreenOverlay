using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrScreenshotView(CVROverlay overlay, Func<int, int, D3D11ControlTexture>? textureFactory) : IDisposable
{
    private readonly OpenVrStaticSurface _button = new(overlay, "io.github.vrphonescreen.overlay.screenshot-button", textureFactory);
    private HmdMatrix34_t _pose;
    private bool _visible;
    private byte[]? _painted;
    private bool _paintedEnabled;
    public void Render(OpenVrPhoneMenuState state, HmdMatrix34_t root, float width, float aspect)
    {
        if (!state.ControlsVisible || state.Translation is null) { Hide(); return; }
        var channel = state.Translation.Screenshot;
        if (channel.Pixels is { } pixels && (pixels != _painted || _paintedEnabled != state.PhoneConnected) && _button.CanUpdatePixels &&
            _button.SetPixels(state.PhoneConnected ? pixels : OpenVrDisabledIcon.Create(pixels), 64, 64))
        { _painted = pixels; _paintedEnabled = state.PhoneConnected; }
        _pose = OpenVrPhoneGroupLayout.SurfacePose(OpenVrPhoneSurface.ScreenshotButton, root, width, aspect, false);
        _visible = _button.Show(_pose, OpenVrPhoneMenuLayout.DotWidth);
    }
    public bool TryPick(HmdMatrix34_t pointer, out OpenVrPhoneMenuHit hit)
    {
        hit = default;
        if (!_visible || !OpenVrPhoneMenuLayout.TryIntersect(pointer, _pose, OpenVrPhoneMenuLayout.DotWidth, 1, out var intersection)) { return false; }
        hit = new(OpenVrMenuTarget.Screenshot, 0, intersection, OpenVrPhoneSurface.ScreenshotButton);
        return true;
    }
    public void Hide() { _button.Hide(); _visible = false; }
    public void Dispose() { _button.Dispose(); }
}
