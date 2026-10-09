using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrPhoneRaycaster
{
    private const float _minimumDistanceMeters = 0.03f;
    private const float _maximumDistanceMeters = 5f;
    private const float _edgeToleranceMeters = 0.0001f;
    private const float _minimumAxisLength = 0.0001f;

    public static bool TryIntersect(
        HmdMatrix34_t pointer,
        HmdMatrix34_t overlayTransform,
        float overlayWidthMeters,
        float overlayAspectRatio,
        out VROverlayIntersectionResults_t hit)
    {
        hit = default;
        if (!float.IsFinite(overlayWidthMeters) ||
            !float.IsFinite(overlayAspectRatio) ||
            overlayWidthMeters <= 0 ||
            overlayAspectRatio <= 0)
        {
            return false;
        }

        float sourceX = pointer.m3;
        float sourceY = pointer.m7;
        float sourceZ = pointer.m11;
        float directionX = -pointer.m2;
        float directionY = -pointer.m6;
        float directionZ = -pointer.m10;
        if (!Normalize(ref directionX, ref directionY, ref directionZ))
        {
            return false;
        }

        float axisX0 = overlayTransform.m0;
        float axisX1 = overlayTransform.m4;
        float axisX2 = overlayTransform.m8;
        if (!Normalize(ref axisX0, ref axisX1, ref axisX2))
        {
            return false;
        }

        float axisY0 = overlayTransform.m1;
        float axisY1 = overlayTransform.m5;
        float axisY2 = overlayTransform.m9;
        float axesDot =
            (axisX0 * axisY0) +
            (axisX1 * axisY1) +
            (axisX2 * axisY2);
        axisY0 -= axesDot * axisX0;
        axisY1 -= axesDot * axisX1;
        axisY2 -= axesDot * axisX2;
        if (!Normalize(ref axisY0, ref axisY1, ref axisY2))
        {
            return false;
        }

        float normalX = (axisX1 * axisY2) - (axisX2 * axisY1);
        float normalY = (axisX2 * axisY0) - (axisX0 * axisY2);
        float normalZ = (axisX0 * axisY1) - (axisX1 * axisY0);
        if (!Normalize(ref normalX, ref normalY, ref normalZ))
        {
            return false;
        }

        float denominator =
            (directionX * normalX) +
            (directionY * normalY) +
            (directionZ * normalZ);
        if (MathF.Abs(denominator) < 0.0001f)
        {
            return false;
        }

        float centerX = overlayTransform.m3;
        float centerY = overlayTransform.m7;
        float centerZ = overlayTransform.m11;
        float distance =
            (((centerX - sourceX) * normalX) +
                ((centerY - sourceY) * normalY) +
                ((centerZ - sourceZ) * normalZ)) /
            denominator;
        if (distance is <= _minimumDistanceMeters or >= _maximumDistanceMeters)
        {
            return false;
        }

        float pointX = sourceX + (directionX * distance);
        float pointY = sourceY + (directionY * distance);
        float pointZ = sourceZ + (directionZ * distance);
        float offsetX = pointX - centerX;
        float offsetY = pointY - centerY;
        float offsetZ = pointZ - centerZ;
        float localX =
            (offsetX * axisX0) +
            (offsetY * axisX1) +
            (offsetZ * axisX2);
        float localY =
            (offsetX * axisY0) +
            (offsetY * axisY1) +
            (offsetZ * axisY2);
        float overlayHeightMeters = overlayWidthMeters / overlayAspectRatio;
        float halfWidth = overlayWidthMeters * 0.5f;
        float halfHeight = overlayHeightMeters * 0.5f;
        if (MathF.Abs(localX) > halfWidth + _edgeToleranceMeters ||
            MathF.Abs(localY) > halfHeight + _edgeToleranceMeters)
        {
            return false;
        }

        float centerDistance = MathF.Sqrt(
            ((centerX - sourceX) * (centerX - sourceX)) +
            ((centerY - sourceY) * (centerY - sourceY)) +
            ((centerZ - sourceZ) * (centerZ - sourceZ)));
        float halfDiagonal = MathF.Sqrt((halfWidth * halfWidth) + (halfHeight * halfHeight));
        float hitDistance = MathF.Sqrt(
            (offsetX * offsetX) +
            (offsetY * offsetY) +
            (offsetZ * offsetZ));
        if (!float.IsFinite(hitDistance) ||
            hitDistance > halfDiagonal + _edgeToleranceMeters ||
            distance > centerDistance + halfDiagonal + _edgeToleranceMeters)
        {
            return false;
        }

        hit.vPoint.v0 = pointX;
        hit.vPoint.v1 = pointY;
        hit.vPoint.v2 = pointZ;
        hit.vNormal.v0 = normalX;
        hit.vNormal.v1 = normalY;
        hit.vNormal.v2 = normalZ;
        hit.vUVs.v0 = Math.Clamp((localX / overlayWidthMeters) + 0.5f, 0, 1);
        hit.vUVs.v1 = Math.Clamp((localY / overlayHeightMeters) + 0.5f, 0, 1);
        hit.fDistance = distance;
        return true;
    }

    private static bool Normalize(ref float x, ref float y, ref float z)
    {
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        if (!float.IsFinite(length) || length < _minimumAxisLength)
        {
            return false;
        }

        x /= length;
        y /= length;
        z /= length;
        return true;
    }
}
