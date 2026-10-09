namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrInteractionGate
{
    public static bool CanAcceptPhoneButtons(bool phoneHit, bool grabbed) =>
        phoneHit && !grabbed;

    public static bool ShouldEmitPhoneButton(
        bool acceptsButtons,
        bool currentPressed,
        bool previousPressed) =>
        acceptsButtons && currentPressed && !previousPressed;

    public static bool IsPhoneHit(
        bool rayIntersectsPhone,
        bool alreadyGrabbed) =>
        !alreadyGrabbed && rayIntersectsPhone;

    public static bool IsVisiblePhoneTarget(
        bool rayIntersectsPhone,
        bool pointerVisible,
        bool alreadyGrabbed) =>
        rayIntersectsPhone && pointerVisible && !alreadyGrabbed;

    public static bool CanStartGrab(
        bool grabPressed,
        bool previousGrabPressed,
        bool alreadyGrabbed,
        bool visiblePhoneHit,
        bool controllerPoseValid) =>
        grabPressed &&
        !previousGrabPressed &&
        !alreadyGrabbed &&
        visiblePhoneHit &&
        controllerPoseValid;

    public static bool ShouldEndGrab(
        bool grabPressed,
        bool alreadyGrabbed,
        bool controllerPoseValid) =>
        alreadyGrabbed && (!grabPressed || !controllerPoseValid);
}

internal sealed class OpenVrPhoneActionSetGate
{
    private const int _releasedPollsRequired = 2;
    private const int _inactivePollsBeforeFallback = 3;
    private int _releasedPolls;
    private int _inactivePolls;

    public bool ShouldEnable(
        bool targetVisible,
        bool rawStateActive,
        bool rawPressed)
    {
        if (!targetVisible)
        {
            Reset();
            return false;
        }

        if (!rawStateActive)
        {
            _releasedPolls = 0;
            _inactivePolls++;
            return _inactivePolls >= _inactivePollsBeforeFallback;
        }

        _inactivePolls = 0;
        if (rawPressed)
        {
            _releasedPolls = 0;
            return false;
        }

        _releasedPolls++;
        return _releasedPolls >= _releasedPollsRequired;
    }

    public void Reset()
    {
        _releasedPolls = 0;
        _inactivePolls = 0;
    }
}

internal static class OpenVrTouchGestureGate
{
    private const float _dragStartThreshold = 0.006f;
    private const float _moveThreshold = 0.001f;
    private const long _minimumMoveIntervalMilliseconds = 12;

    public static bool ShouldStartDrag(
        float startX,
        float startY,
        float currentX,
        float currentY)
    {
        float deltaX = currentX - startX;
        float deltaY = currentY - startY;
        return (deltaX * deltaX) + (deltaY * deltaY) >=
            _dragStartThreshold * _dragStartThreshold;
    }

    public static bool ShouldSendMove(
        float lastX,
        float lastY,
        float currentX,
        float currentY,
        long elapsedMilliseconds)
    {
        if (elapsedMilliseconds < _minimumMoveIntervalMilliseconds)
        {
            return false;
        }

        float deltaX = currentX - lastX;
        float deltaY = currentY - lastY;
        return (deltaX * deltaX) + (deltaY * deltaY) >=
            _moveThreshold * _moveThreshold;
    }
}
