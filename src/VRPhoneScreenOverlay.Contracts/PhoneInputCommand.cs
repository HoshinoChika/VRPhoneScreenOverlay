namespace VRPhoneScreenOverlay.Contracts;

public enum PhoneInputCommandKind
{
    PointerDown,
    PointerMove,
    PointerUp,
    PointerCancel,
    Back,
    Home,
    RecentApps,
    OpenControlPanel,
    Screenshot,
    Scroll,
    WakeScreen,
    UserActivity,
    UnlockDigit,
    UnlockBackspace,
    UnlockConfirm,
    TurnDisplayOff,
    RestoreDisplayPower,
    MaintainDisplayOff,
    WakeScreenWhileDisplayOff,
    ToggleScreenGuard,
}

public readonly record struct PhoneInputCommand(
    long Sequence,
    PhoneInputCommandKind Kind,
    long PointerId,
    float NormalizedX,
    float NormalizedY,
    int ScreenWidth,
    int ScreenHeight,
    DateTimeOffset CreatedAt,
    float ScrollDelta = 0,
    int UnlockDigit = -1);
