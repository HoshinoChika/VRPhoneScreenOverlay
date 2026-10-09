namespace VRPhoneScreenOverlay.Contracts;

public enum AppLifecycleState
{
    Created,
    Starting,
    Ready,
    Degraded,
    Stopping,
    Stopped,
}

public readonly record struct StateReason(string Code, string Message)
{
    public static StateReason Normal(string code, string message) => new(code, message);
}

public sealed record AppRuntimeSnapshot(
    AppLifecycleState State,
    StateReason Reason,
    DateTimeOffset ChangedAt);
