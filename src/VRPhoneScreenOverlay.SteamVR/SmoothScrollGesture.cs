using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

internal readonly record struct SmoothScrollCommand(
    PhoneInputCommandKind Kind,
    float NormalizedX,
    float NormalizedY);

internal sealed class SmoothScrollGesture
{
    private const float _deadzone = 0.28f;
    private const float _minimumY = 0.08f;
    private const float _maximumY = 0.92f;
    private const float _minimumSpeed = 0.18f;
    private const float _linearSpeedRange = 0.82f;
    private const float _fullStrengthBoost = 0.8f;
    private const long _moveIntervalMilliseconds = 12;
    private const long _restartDelayMilliseconds = 20;
    private bool _active;
    private float _x;
    private float _y;
    private long _lastMoveTimestamp;
    private long _nextStartTimestamp;

    public bool Active => _active;

    public int Update(
        float axisY,
        bool enabled,
        float pointerX,
        float pointerY,
        long timestamp,
        Span<SmoothScrollCommand> commands)
    {
        if (!enabled || !float.IsFinite(axisY) || MathF.Abs(axisY) < _deadzone)
        {
            return Stop(commands);
        }

        if (!_active)
        {
            if (timestamp < _nextStartTimestamp)
            {
                return 0;
            }

            _active = true;
            _x = Math.Clamp(pointerX, 0, 1);
            _y = Math.Clamp(pointerY, 0.25f, 0.75f);
            _lastMoveTimestamp = timestamp;
            commands[0] = new SmoothScrollCommand(
                PhoneInputCommandKind.PointerDown,
                _x,
                _y);
            return 1;
        }

        long elapsedMilliseconds = timestamp - _lastMoveTimestamp;
        if (elapsedMilliseconds < _moveIntervalMilliseconds)
        {
            return 0;
        }

        float elapsedSeconds = Math.Clamp(elapsedMilliseconds / 1000f, 0.001f, 0.05f);
        float strength = Math.Clamp(
            (MathF.Abs(axisY) - _deadzone) / (1f - _deadzone),
            0,
            1);
        float speed = CalculateSpeed(strength);
        float nextY = _y + (MathF.CopySign(speed, axisY) * elapsedSeconds);
        _lastMoveTimestamp = timestamp;
        if (nextY is < _minimumY or > _maximumY)
        {
            _y = Math.Clamp(nextY, _minimumY, _maximumY);
            commands[0] = new SmoothScrollCommand(
                PhoneInputCommandKind.PointerUp,
                _x,
                _y);
            _active = false;
            _nextStartTimestamp = timestamp + _restartDelayMilliseconds;
            return 1;
        }

        _y = nextY;
        commands[0] = new SmoothScrollCommand(
            PhoneInputCommandKind.PointerMove,
            _x,
            _y);
        return 1;
    }

    public int Stop(Span<SmoothScrollCommand> commands) =>
        Finish(PhoneInputCommandKind.PointerUp, commands);

    public int Cancel(Span<SmoothScrollCommand> commands) =>
        Finish(PhoneInputCommandKind.PointerCancel, commands);

    internal static float CalculateSpeed(float strength)
    {
        float normalizedStrength = Math.Clamp(strength, 0, 1);
        return _minimumSpeed +
            (_linearSpeedRange * normalizedStrength) +
            (_fullStrengthBoost * normalizedStrength * normalizedStrength);
    }

    private int Finish(
        PhoneInputCommandKind kind,
        Span<SmoothScrollCommand> commands)
    {
        if (!_active)
        {
            return 0;
        }

        commands[0] = new SmoothScrollCommand(kind, _x, _y);
        _active = false;
        _nextStartTimestamp = 0;
        return 1;
    }
}
