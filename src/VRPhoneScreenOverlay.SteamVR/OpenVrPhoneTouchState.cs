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
    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }
    public bool GeometryAwaitingRelease { get; private set; }

    public bool TrySetGeometry(int width, int height, out OpenVrPhoneInputCommand cancel)
    {
        cancel = default;
        if (width < 1 || height < 1 || (FrameWidth == width && FrameHeight == height)) { return false; }
        bool changed = FrameWidth > 0;
        bool cancelled = changed && RequireGeometryRelease(out cancel);
        FrameWidth = width; FrameHeight = height;
        return cancelled;
    }

    public bool RequireGeometryRelease(out OpenVrPhoneInputCommand cancel)
    {
        GeometryAwaitingRelease = true;
        return TryCancel(out cancel);
    }

    public void ObserveGeometryRelease(bool actionAvailable, bool pressed)
    {
        if (GeometryAwaitingRelease && actionAvailable && !pressed)
        { GeometryAwaitingRelease = false; _previousTouch = false; }
    }

    public bool TryUpdate(
        bool touch,
        float pointerX,
        float pointerY,
        long timestamp,
        out OpenVrPhoneInputCommand command)
    {
        if (GeometryAwaitingRelease) { command = default; return false; }
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
                pointerY)
            { ScreenWidth = FrameWidth, ScreenHeight = FrameHeight };
            return true;
        }

        if (!touch && IsDown)
        {
            float releaseX = _dragging ? pointerX : _downStartX;
            float releaseY = _dragging ? pointerY : _downStartY;
            command = new OpenVrPhoneInputCommand(
                PhoneInputCommandKind.PointerUp,
                releaseX,
                releaseY)
            { ScreenWidth = FrameWidth, ScreenHeight = FrameHeight };
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
                pointerY)
            { ScreenWidth = FrameWidth, ScreenHeight = FrameHeight };
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

        command = new OpenVrPhoneInputCommand(kind, _lastX, _lastY) { ScreenWidth = FrameWidth, ScreenHeight = FrameHeight };
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
