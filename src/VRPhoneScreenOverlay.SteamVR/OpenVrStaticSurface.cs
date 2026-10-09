using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

// A failed control refresh must not tear down the phone input poller. Keep the
// last submitted texture and retry at most once a second. Calls hold ApiGate.
internal sealed class OpenVrStaticSurface(CVROverlay overlay, string key,
    Func<int, int, D3D11ControlTexture>? textureFactory = null) : IDisposable
{
    private ulong _handle;
    private bool _visible;
    private bool _textureReady;
    private float _alpha = 1;
    private D3D11ControlTexture? _texture;
    private long _retryAfter;
    public long FailedUpdates { get; private set; }
    public bool CanUpdatePixels => Environment.TickCount64 >= _retryAfter;

    public bool SetPixels(byte[] pixels, int width, int height)
    {
        if (Environment.TickCount64 < _retryAfter) { return false; }
        try
        {
            if (_handle == 0)
            {
                Check(overlay.CreateOverlay(key, "VRPhoneScreen Overlay controls", ref _handle));
                Check(overlay.SetOverlayInputMethod(_handle, VROverlayInputMethod.None));
            }
            _texture ??= (textureFactory ?? D3D11ControlTexture.CreateForSteamVr)(width, height);
            if (_texture.Width != width || _texture.Height != height)
            {
                throw new ArgumentException("Control surface dimensions must remain fixed.");
            }
            _texture.Update(pixels);
            Texture_t texture = new() { handle = _texture.NativePointer, eType = ETextureType.DirectX, eColorSpace = EColorSpace.Auto };
            Check(overlay.SetOverlayTexture(_handle, ref texture));
            _texture.FlushSubmission();
            _textureReady = true;
            return true;
        }
        catch (Exception exception) when (exception is SharpGenException or COMException or OpenVrOverlayException or InvalidOperationException)
        {
            FailedUpdates++;
            _retryAfter = Environment.TickCount64 + 1000;
            return false;
        }
    }

    public bool Show(HmdMatrix34_t transform, float width, float texelAspect = 1f, float alpha = 1f)
    {
        if (!_textureReady) { return false; }
        if (!CanUpdatePixels) { return _visible; }
        try
        {
            alpha = Math.Clamp(alpha, 0, 1);
            if (_alpha != alpha)
            {
                Check(overlay.SetOverlayAlpha(_handle, alpha));
                _alpha = alpha;
            }
            Check(overlay.SetOverlayWidthInMeters(_handle, width));
            Check(overlay.SetOverlayTexelAspect(_handle, texelAspect));
            Check(overlay.SetOverlayTransformAbsolute(_handle,
                ETrackingUniverseOrigin.TrackingUniverseStanding, ref transform));
            if (!_visible)
            {
                Check(overlay.ShowOverlay(_handle));
                _visible = true;
            }
            return true;
        }
        catch (OpenVrOverlayException)
        {
            FailedUpdates++;
            _retryAfter = Environment.TickCount64 + 1000;
            return _visible;
        }
    }

    public void Hide()
    {
        if (_visible)
        {
            _ = overlay.HideOverlay(_handle);
            _visible = false;
        }
    }

    public void Dispose()
    {
        Hide();
        if (_handle != 0)
        {
            _ = overlay.ClearOverlayTexture(_handle);
            _ = overlay.DestroyOverlay(_handle);
            _handle = 0;
        }
        _textureReady = false;
        _texture?.Dispose();
        _texture = null;
    }

    private static void Check(EVROverlayError error)
    {
        if (error != EVROverlayError.None)
        {
            throw new OpenVrOverlayException("OPENVR_STATIC_SURFACE_FAILED", "SteamVR 附属浮窗暂时不可用");
        }
    }
}
