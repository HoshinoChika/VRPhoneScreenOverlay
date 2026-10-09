using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrPhoneTouchState
{
    private bool _previousTouch;
    private bool _dragging;
    private float _downStartX;
    private float _downStartY;
    private float _lastX;
    private float _lastY;
    private long _lastMoveTimestamp;

    public bool IsDown { get; private set; }

    public bool TryUpdate(
        bool touch,
        float pointerX,
        float pointerY,
        long timestamp,
        out OpenVrPhoneInputCommand command)
    {
        if (touch && !_previousTouch && !IsDown)
        {
            IsDown = true;
            _dragging = false;
            _downStartX = pointerX;
            _downStartY = pointerY;
            _lastX = pointerX;
            _lastY = pointerY;
            _lastMoveTimestamp = timestamp;
            command = new OpenVrPhoneInputCommand(
                PhoneInputCommandKind.PointerDown,
                pointerX,
                pointerY);
            return true;
        }

        if (!touch && IsDown)
        {
            float releaseX = _dragging ? pointerX : _downStartX;
            float releaseY = _dragging ? pointerY : _downStartY;
            command = new OpenVrPhoneInputCommand(
                PhoneInputCommandKind.PointerUp,
                releaseX,
                releaseY);
            ResetPointer(releaseX, releaseY);
            return true;
        }

        if (touch && IsDown && !_dragging &&
            OpenVrTouchGestureGate.ShouldStartDrag(
                _downStartX,
                _downStartY,
                pointerX,
                pointerY))
        {
            _dragging = true;
        }

        if (touch && IsDown && _dragging &&
            OpenVrTouchGestureGate.ShouldSendMove(
                _lastX,
                _lastY,
                pointerX,
                pointerY,
                timestamp - _lastMoveTimestamp))
        {
            command = new OpenVrPhoneInputCommand(
                PhoneInputCommandKind.PointerMove,
                pointerX,
                pointerY);
            _lastX = pointerX;
            _lastY = pointerY;
            _lastMoveTimestamp = timestamp;
            return true;
        }

        command = default;
        return false;
    }

    public bool TryRelease(out OpenVrPhoneInputCommand command) =>
        TryFinish(PhoneInputCommandKind.PointerUp, out command);

    public bool TryCancel(out OpenVrPhoneInputCommand command) =>
        TryFinish(PhoneInputCommandKind.PointerCancel, out command);

    public void RecordTouchState(bool touch)
    {
        _previousTouch = touch;
    }

    private bool TryFinish(
        PhoneInputCommandKind kind,
        out OpenVrPhoneInputCommand command)
    {
        if (!IsDown)
        {
            command = default;
            return false;
        }

        command = new OpenVrPhoneInputCommand(kind, _lastX, _lastY);
        ResetPointer(_lastX, _lastY);
        return true;
    }

    private void ResetPointer(float lastX, float lastY)
    {
        IsDown = false;
        _dragging = false;
        _lastX = lastX;
        _lastY = lastY;
    }
}
