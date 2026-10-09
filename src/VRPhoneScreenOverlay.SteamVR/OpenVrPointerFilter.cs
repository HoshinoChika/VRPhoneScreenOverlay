using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrPointerSmoother
{
    private const float _deadZone = 0.0015f;
    private const float _slowTimeConstantSeconds = 0.045f;
    private const float _fastTimeConstantSeconds = 0.008f;
    private const float _fullSpeedDistance = 0.05f;

    private bool _initialized;
    private long _lastTimestamp;
    private VROverlayIntersectionResults_t _filtered;

    public VROverlayIntersectionResults_t Update(
        VROverlayIntersectionResults_t raw,
        long timestamp)
    {
        if (!_initialized)
        {
            _initialized = true;
            _lastTimestamp = timestamp;
            _filtered = raw;
            return raw;
        }

        float elapsedSeconds = Math.Clamp(
            (timestamp - _lastTimestamp) / 1000f,
            0.001f,
            0.05f);
        _lastTimestamp = timestamp;
        float deltaU = raw.vUVs.v0 - _filtered.vUVs.v0;
        float deltaV = raw.vUVs.v1 - _filtered.vUVs.v1;
        float distance = MathF.Sqrt((deltaU * deltaU) + (deltaV * deltaV));
        if (distance <= _deadZone)
        {
            return _filtered;
        }

        float responsiveness = Math.Clamp(
            (distance - _deadZone) / (_fullSpeedDistance - _deadZone),
            0,
            1);
        float timeConstant =
            _slowTimeConstantSeconds +
            ((_fastTimeConstantSeconds - _slowTimeConstantSeconds) * responsiveness);
        float alpha = 1f - MathF.Exp(-elapsedSeconds / timeConstant);
        _filtered.vPoint.v0 = Lerp(_filtered.vPoint.v0, raw.vPoint.v0, alpha);
        _filtered.vPoint.v1 = Lerp(_filtered.vPoint.v1, raw.vPoint.v1, alpha);
        _filtered.vPoint.v2 = Lerp(_filtered.vPoint.v2, raw.vPoint.v2, alpha);
        _filtered.vNormal = raw.vNormal;
        _filtered.vUVs.v0 = Lerp(_filtered.vUVs.v0, raw.vUVs.v0, alpha);
        _filtered.vUVs.v1 = Lerp(_filtered.vUVs.v1, raw.vUVs.v1, alpha);
        _filtered.fDistance = Lerp(_filtered.fDistance, raw.fDistance, alpha);
        return _filtered;
    }

    public void Reset()
    {
        _initialized = false;
        _lastTimestamp = 0;
        _filtered = default;
    }

    private static float Lerp(float from, float to, float amount) =>
        from + ((to - from) * amount);
}
