namespace VRPhoneScreenOverlay.Core;

/// <summary>
/// Stable reason codes raised by this module. Values are protocol: they are only ever
/// added, never re-spelled, and never localised.
/// </summary>
public static class AppReasonCodes
{
    /// <summary><c>APP_CREATED</c></summary>
    public const string Created = "APP_CREATED";

    /// <summary><c>APP_NOT_STARTED</c></summary>
    public const string NotStarted = "APP_NOT_STARTED";

    /// <summary><c>APP_READY</c></summary>
    public const string Ready = "APP_READY";

    /// <summary><c>APP_STARTING</c></summary>
    public const string Starting = "APP_STARTING";

    /// <summary><c>APP_STOPPED</c></summary>
    public const string Stopped = "APP_STOPPED";

    /// <summary><c>APP_STOPPING</c></summary>
    public const string Stopping = "APP_STOPPING";
}
