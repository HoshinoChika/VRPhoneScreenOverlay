using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrPhoneBackface(CVROverlay overlay, Func<int, int, D3D11ControlTexture>? textureFactory = null) : IDisposable
{
    private readonly OpenVrStaticSurface _surface = new(overlay, "io.github.vrphonescreen.overlay.phone.back", textureFactory);
    private readonly OpenVrStaticSurface _rim = new(overlay, "io.github.vrphonescreen.overlay.phone.rim", textureFactory);
    private readonly OpenVrStaticSurface _plate = new(overlay, "io.github.vrphonescreen.overlay.phone.plate", textureFactory);
    private readonly OpenVrStaticSurface _backRim = new(overlay, "io.github.vrphonescreen.overlay.phone.back-rim", textureFactory);
    private float _paintedAspect;

    public void Update(bool visible, HmdMatrix34_t front, float width, float aspect, float opacity = 1f)
    {
        if (!visible)
        {
            _surface.Hide();
            _rim.Hide();
            _plate.Hide();
            _backRim.Hide();
            return;
        }

        if (_paintedAspect != aspect && _surface.CanUpdatePixels && _rim.CanUpdatePixels && _plate.CanUpdatePixels && _backRim.CanUpdatePixels)
        {
            // Four fixed-size surfaces: two fading plates and two hollow rims.
            // Only aspect changes upload pixels; opacity uses native alpha.
            byte[] pixels = OpenVrPhoneShellGeometry.Pixels(aspect);
            byte[] rimPixels = OpenVrPhoneShellGeometry.Pixels(aspect, rimOnly: true);
            bool back = _surface.SetPixels(pixels, OpenVrPhoneShellGeometry.TextureSize, OpenVrPhoneShellGeometry.TextureSize);
            bool frontReady = _rim.SetPixels(rimPixels, OpenVrPhoneShellGeometry.TextureSize, OpenVrPhoneShellGeometry.TextureSize);
            bool plate = _plate.SetPixels(pixels, OpenVrPhoneShellGeometry.TextureSize, OpenVrPhoneShellGeometry.TextureSize);
            bool backRim = _backRim.SetPixels(rimPixels, OpenVrPhoneShellGeometry.TextureSize, OpenVrPhoneShellGeometry.TextureSize);
            if (back && frontReady && plate && backRim) { _paintedAspect = aspect; }
        }

        var outer = OpenVrPhoneShellGeometry.OuterSize(width, aspect);
        HmdMatrix34_t behind = OpenVrTransformMath.Identity();
        behind.m11 = -0.0005f;
        _plate.Show(OpenVrTransformMath.Multiply(front, behind), outer.Width, outer.Aspect, opacity);
        behind.m11 = -0.00025f;
        _rim.Show(OpenVrTransformMath.Multiply(front, behind), outer.Width, outer.Aspect);
        _surface.Show(BackTransform(front), outer.Width, outer.Aspect, opacity);
        HmdMatrix34_t rearRim = OpenVrTransformMath.Identity();
        rearRim.m0 = -1;
        rearRim.m10 = -1;
        rearRim.m11 = -0.00125f;
        _backRim.Show(OpenVrTransformMath.Multiply(front, rearRim), outer.Width, outer.Aspect);
    }

    internal static HmdMatrix34_t BackTransform(HmdMatrix34_t front)
    {
        HmdMatrix34_t relative = OpenVrTransformMath.Identity();
        relative.m0 = -1;
        relative.m10 = -1;
        relative.m11 = -0.001f;
        return OpenVrTransformMath.Multiply(front, relative);
    }

    public void Dispose() { _backRim.Dispose(); _plate.Dispose(); _rim.Dispose(); _surface.Dispose(); }
}
