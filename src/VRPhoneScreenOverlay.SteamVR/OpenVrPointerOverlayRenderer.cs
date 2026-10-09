using System.Runtime.InteropServices;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrPointerOverlayRenderer(CVROverlay overlay) : IDisposable
{
    private const float _cursorWidthMeters = 0.014f;
    private readonly CVROverlay _overlay = overlay;
    private ulong _cursorOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
    private ulong _rayOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
    private bool _cursorVisible;
    private bool _rayVisible;

    public bool TryUpdate(
        HmdMatrix34_t pointer,
        VROverlayIntersectionResults_t hit,
        float viewerX,
        float viewerY,
        float viewerZ)
    {
        if (!EnsureCursor() || !EnsureRay())
        {
            Hide();
            return false;
        }

        float towardViewerX = viewerX - hit.vPoint.v0;
        float towardViewerY = viewerY - hit.vPoint.v1;
        float towardViewerZ = viewerZ - hit.vPoint.v2;
        NormalizeOrFallback(
            ref towardViewerX,
            ref towardViewerY,
            ref towardViewerZ,
            hit.vNormal.v0,
            hit.vNormal.v1,
            hit.vNormal.v2);
        HmdMatrix34_t cursorTransform = CreateBillboardTransform(
            hit.vPoint.v0 + (towardViewerX * 0.0012f),
            hit.vPoint.v1 + (towardViewerY * 0.0012f),
            hit.vPoint.v2 + (towardViewerZ * 0.0012f),
            towardViewerX,
            towardViewerY,
            towardViewerZ);
        EVROverlayError transformError = _overlay.SetOverlayTransformAbsolute(
            _cursorOverlayHandle,
            ETrackingUniverseOrigin.TrackingUniverseStanding,
            ref cursorTransform);
        if (transformError != EVROverlayError.None)
        {
            DestroyCursor();
            Hide();
            return false;
        }

        float sourceX = pointer.m3;
        float sourceY = pointer.m7;
        float sourceZ = pointer.m11;
        float rayX = hit.vPoint.v0 - sourceX;
        float rayY = hit.vPoint.v1 - sourceY;
        float rayZ = hit.vPoint.v2 - sourceZ;
        float rayLength = MathF.Sqrt((rayX * rayX) + (rayY * rayY) + (rayZ * rayZ));
        if (rayLength > 0.001f)
        {
            rayX /= rayLength;
            rayY /= rayLength;
            rayZ /= rayLength;
            float midpointX = (sourceX + hit.vPoint.v0) * 0.5f;
            float midpointY = (sourceY + hit.vPoint.v1) * 0.5f;
            float midpointZ = (sourceZ + hit.vPoint.v2) * 0.5f;
            float normalX = viewerX - midpointX;
            float normalY = viewerY - midpointY;
            float normalZ = viewerZ - midpointZ;
            float alongRay = (normalX * rayX) + (normalY * rayY) + (normalZ * rayZ);
            normalX -= alongRay * rayX;
            normalY -= alongRay * rayY;
            normalZ -= alongRay * rayZ;
            NormalizeOrFallback(
                ref normalX,
                ref normalY,
                ref normalZ,
                hit.vNormal.v0,
                hit.vNormal.v1,
                hit.vNormal.v2);
            float upX = (normalY * rayZ) - (normalZ * rayY);
            float upY = (normalZ * rayX) - (normalX * rayZ);
            float upZ = (normalX * rayY) - (normalY * rayX);
            HmdMatrix34_t rayTransform = CreateTransform(
                rayX,
                rayY,
                rayZ,
                upX,
                upY,
                upZ,
                normalX,
                normalY,
                normalZ,
                midpointX,
                midpointY,
                midpointZ);
            EVROverlayError rayTransformError = _overlay.SetOverlayTransformAbsolute(
                _rayOverlayHandle,
                ETrackingUniverseOrigin.TrackingUniverseStanding,
                ref rayTransform);
            if (rayTransformError != EVROverlayError.None)
            {
                Dispose();
                return false;
            }

            if (_overlay.SetOverlayWidthInMeters(_rayOverlayHandle, rayLength) !=
                EVROverlayError.None)
            {
                Dispose();
                return false;
            }
        }

        if (!_cursorVisible)
        {
            if (_overlay.ShowOverlay(_cursorOverlayHandle) != EVROverlayError.None)
            {
                Hide();
                return false;
            }

            _cursorVisible = true;
        }

        if (!_rayVisible)
        {
            if (_overlay.ShowOverlay(_rayOverlayHandle) != EVROverlayError.None)
            {
                Hide();
                return false;
            }

            _rayVisible = true;
        }

        return _cursorVisible && _rayVisible;
    }

    public void Hide()
    {
        HideCursor();
        if (_rayVisible && _rayOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
        {
            _ = _overlay.HideOverlay(_rayOverlayHandle);
            _rayVisible = false;
        }
    }

    public void Dispose()
    {
        DestroyCursor();
        if (_rayOverlayHandle == OpenVR.k_ulOverlayHandleInvalid)
        {
            return;
        }

        _ = _overlay.HideOverlay(_rayOverlayHandle);
        _ = _overlay.ClearOverlayTexture(_rayOverlayHandle);
        _ = _overlay.DestroyOverlay(_rayOverlayHandle);
        _rayOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
        _rayVisible = false;
    }

    private bool EnsureCursor()
    {
        if (_cursorOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
        {
            return true;
        }

        EVROverlayError createError = _overlay.CreateOverlay(
            "io.github.vrphonescreen.overlay.pointer",
            "VRPhoneScreen Overlay - Pointer",
            ref _cursorOverlayHandle);
        if (createError != EVROverlayError.None)
        {
            _cursorOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
            return false;
        }

        _ = _overlay.SetOverlayInputMethod(_cursorOverlayHandle, VROverlayInputMethod.None);
        _ = _overlay.SetOverlayAlpha(_cursorOverlayHandle, 0.95f);
        _ = _overlay.SetOverlayWidthInMeters(_cursorOverlayHandle, _cursorWidthMeters);
        _ = _overlay.SetOverlaySortOrder(_cursorOverlayHandle, 100);

        byte[] pixels = CreatePointerPixels();
        GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            EVROverlayError textureError = _overlay.SetOverlayRaw(
                _cursorOverlayHandle,
                pinned.AddrOfPinnedObject(),
                32,
                32,
                4);
            if (textureError != EVROverlayError.None)
            {
                DestroyCursor();
                return false;
            }
        }
        finally
        {
            pinned.Free();
        }

        return true;
    }

    private bool EnsureRay()
    {
        if (_rayOverlayHandle != OpenVR.k_ulOverlayHandleInvalid)
        {
            return true;
        }

        EVROverlayError createError = _overlay.CreateOverlay(
            "io.github.vrphonescreen.overlay.ray",
            "VRPhoneScreen Overlay - Ray",
            ref _rayOverlayHandle);
        if (createError != EVROverlayError.None)
        {
            _rayOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
            return false;
        }

        _ = _overlay.SetOverlayInputMethod(_rayOverlayHandle, VROverlayInputMethod.None);
        _ = _overlay.SetOverlayAlpha(_rayOverlayHandle, 0.8f);
        _ = _overlay.SetOverlaySortOrder(_rayOverlayHandle, 99);
        byte[] pixels = CreateRayPixels();
        GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            EVROverlayError textureError = _overlay.SetOverlayRaw(
                _rayOverlayHandle,
                pinned.AddrOfPinnedObject(),
                512,
                4,
                4);
            if (textureError != EVROverlayError.None)
            {
                Dispose();
                return false;
            }
        }
        finally
        {
            pinned.Free();
        }

        return true;
    }

    private void HideCursor()
    {
        if (!_cursorVisible || _cursorOverlayHandle == OpenVR.k_ulOverlayHandleInvalid)
        {
            return;
        }

        _ = _overlay.HideOverlay(_cursorOverlayHandle);
        _cursorVisible = false;
    }

    private void DestroyCursor()
    {
        if (_cursorOverlayHandle == OpenVR.k_ulOverlayHandleInvalid)
        {
            return;
        }

        _ = _overlay.HideOverlay(_cursorOverlayHandle);
        _ = _overlay.ClearOverlayTexture(_cursorOverlayHandle);
        _ = _overlay.DestroyOverlay(_cursorOverlayHandle);
        _cursorOverlayHandle = OpenVR.k_ulOverlayHandleInvalid;
        _cursorVisible = false;
    }

    private static byte[] CreatePointerPixels()
    {
        byte[] pixels = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                float offsetX = x - 15.5f;
                float offsetY = y - 15.5f;
                float radiusSquared = (offsetX * offsetX) + (offsetY * offsetY);
                int index = ((y * 32) + x) * 4;
                bool inside = radiusSquared <= 12.5f * 12.5f;
                bool center = radiusSquared <= 5f * 5f;
                pixels[index] = center ? (byte)72 : (byte)255;
                pixels[index + 1] = center ? (byte)180 : (byte)255;
                pixels[index + 2] = 255;
                pixels[index + 3] = inside ? (byte)245 : (byte)0;
            }
        }

        return pixels;
    }

    private static byte[] CreateRayPixels()
    {
        byte[] pixels = new byte[512 * 4 * 4];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 512; x++)
            {
                int index = ((y * 512) + x) * 4;
                pixels[index] = 72;
                pixels[index + 1] = 190;
                pixels[index + 2] = 255;
                pixels[index + 3] = y is 1 or 2 ? (byte)230 : (byte)90;
            }
        }

        return pixels;
    }

    private static HmdMatrix34_t CreateBillboardTransform(
        float positionX,
        float positionY,
        float positionZ,
        float normalX,
        float normalY,
        float normalZ)
    {
        float rightX = normalZ;
        float rightY = 0;
        float rightZ = -normalX;
        NormalizeOrFallback(ref rightX, ref rightY, ref rightZ, 1, 0, 0);
        float upX = (normalY * rightZ) - (normalZ * rightY);
        float upY = (normalZ * rightX) - (normalX * rightZ);
        float upZ = (normalX * rightY) - (normalY * rightX);
        return CreateTransform(
            rightX,
            rightY,
            rightZ,
            upX,
            upY,
            upZ,
            normalX,
            normalY,
            normalZ,
            positionX,
            positionY,
            positionZ);
    }

    private static HmdMatrix34_t CreateTransform(
        float xAxisX,
        float xAxisY,
        float xAxisZ,
        float yAxisX,
        float yAxisY,
        float yAxisZ,
        float zAxisX,
        float zAxisY,
        float zAxisZ,
        float positionX,
        float positionY,
        float positionZ) => new()
        {
            m0 = xAxisX,
            m1 = yAxisX,
            m2 = zAxisX,
            m3 = positionX,
            m4 = xAxisY,
            m5 = yAxisY,
            m6 = zAxisY,
            m7 = positionY,
            m8 = xAxisZ,
            m9 = yAxisZ,
            m10 = zAxisZ,
            m11 = positionZ,
        };

    private static void NormalizeOrFallback(
        ref float x,
        ref float y,
        ref float z,
        float fallbackX,
        float fallbackY,
        float fallbackZ)
    {
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        if (length < 0.00001f)
        {
            x = fallbackX;
            y = fallbackY;
            z = fallbackZ;
            length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        }

        if (length < 0.00001f)
        {
            x = 0;
            y = 0;
            z = 1;
            return;
        }

        x /= length;
        y /= length;
        z /= length;
    }
}
