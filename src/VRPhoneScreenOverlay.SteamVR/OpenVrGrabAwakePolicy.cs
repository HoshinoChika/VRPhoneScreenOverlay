namespace VRPhoneScreenOverlay.SteamVR;

internal enum OpenVrGrabAwakeAction
{
    None,
    Wake,
    UserActivity,
}

internal sealed class OpenVrGrabAwakePolicy
{
    private const long _userActivityIntervalMilliseconds = 3_000;
    private bool _previouslyGrabbed;
    private long _nextUserActivityAt;

    public OpenVrGrabAwakeAction NextAction(
        bool grabbed,
        bool keepAwakeWhileGrabbed,
        long now)
    {
        bool started = grabbed && !_previouslyGrabbed;
        bool refreshUserActivity = grabbed &&
            keepAwakeWhileGrabbed &&
            !started &&
            now >= _nextUserActivityAt;
        if (started || refreshUserActivity)
        {
            _nextUserActivityAt = checked(now + _userActivityIntervalMilliseconds);
        }
        else if (!grabbed)
        {
            _nextUserActivityAt = 0;
        }

        _previouslyGrabbed = grabbed;
        return started
            ? OpenVrGrabAwakeAction.Wake
            : refreshUserActivity
                ? OpenVrGrabAwakeAction.UserActivity
                : OpenVrGrabAwakeAction.None;
    }
}
