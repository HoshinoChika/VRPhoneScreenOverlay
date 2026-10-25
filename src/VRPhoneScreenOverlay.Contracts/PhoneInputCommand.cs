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
    SynchronizeDisplayGeometry,
}

// A completed sender operation may be metadata maintenance or deliberately
// suppressed input. Neither is a control-channel write or an execution receipt.
public readonly record struct PhoneInputWriteResult(bool Written, bool PointerTerminated = false, string? SuppressionReason = null);

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
    int UnlockDigit = -1)
{
    public long? GeometryRevision { get; init; }
}
