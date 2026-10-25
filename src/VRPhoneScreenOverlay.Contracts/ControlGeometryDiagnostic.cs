namespace VRPhoneScreenOverlay.Contracts;

public sealed record ControlGeometryDiagnostic(int NativeWidth, int NativeHeight, int VideoWidth, int VideoHeight,
    long Revision, long RefreshAttempts, long RefreshFailures, long CancelAttempts, long CancelWrites,
    long SuppressedCommands, bool WaitingForNewDown, DateTimeOffset? LastVerifiedAt, string Reason)
{
    public int? Rotation { get; init; }
    public string RotationStatus { get; init; } = "not-observed";
    public int ConsecutiveRefreshTimeouts { get; init; }
    public bool NativeSizeUnverified { get; init; }
}
