namespace VRPhoneScreenOverlay.Settings;

public sealed record VideoParameters(int ResolutionPercent, int BitrateMbps, int MaximumFramesPerSecond)
{
    public static VideoParameters From(AppSettings settings) => new(settings.VideoResolutionPercent, settings.VideoBitrateMbps, settings.VideoMaximumFramesPerSecond);
    public AppSettings Apply(AppSettings settings) => settings with
    {
        VideoResolutionPercent = ResolutionPercent,
        VideoBitrateMbps = BitrateMbps,
        VideoMaximumFramesPerSecond = MaximumFramesPerSecond
    };
    public bool IsValid => ResolutionPercent is >= 1 and <= 100 && BitrateMbps is >= 1 and <= 100 && MaximumFramesPerSecond is >= 1 and <= 120;
}

public sealed record VideoTrialContext(string DeviceKey, int NativeWidth, int NativeHeight, string DecoderMode);
public sealed record VideoFrameEvidence(int VisibleWidth, int VisibleHeight, int BufferWidth, int BufferHeight,
    int Stride, int Bytes, string DecoderBackend, string GraphicsBackend, bool BackendNamesTruncated = false);
public sealed record VideoTrialAttempt(string Id, string Scope, VideoParameters Desired, VideoParameters Applied,
    DateTimeOffset StartedAt, string State, DateTimeOffset? FirstFrameAt = null, DateTimeOffset? StableAt = null,
    VideoFrameEvidence? Frame = null, string? EndReason = null, double StableAfterMilliseconds = 0, long StableFrameCount = 0)
{
    public string Build { get; init; } = "unknown";
    public string DecoderMode { get; init; } = "automatic";
    public int NativeWidth { get; init; }
    public int NativeHeight { get; init; }
    public bool RequiresManualRetry { get; init; }
    public bool BlocksAutomatic => State == "interrupted" || State == "failed" && (RequiresManualRetry || FirstFrameAt is null);
}
public sealed record VideoParameterChange(string Id, string Scope, VideoParameters Previous, VideoParameters Candidate, DateTimeOffset StartedAt, string Outcome = "pending")
{
    public string Build { get; init; } = "unknown";
}
public sealed record VideoSuccessProfile(string Scope, VideoTrialAttempt? LastFirstFrame,
    VideoTrialAttempt? PreviousFirstFrame, VideoTrialAttempt? LastStable);
public sealed record VideoRecoveryCandidate(VideoParameters Parameters, string Verification, DateTimeOffset ConfirmedAt);
public sealed record VideoTrialDocument(int SchemaVersion, VideoTrialAttempt? Active, VideoTrialAttempt? LastAttempt,
    VideoParameterChange? PendingChange, VideoSuccessProfile[] Profiles, VideoTrialAttempt[] RecentAttempts, VideoParameterChange[] RecentChanges,
    bool FailureAcknowledged = false, int OmittedProfiles = 0, int OmittedAttempts = 0, int OmittedChanges = 0,
    bool RequireManualStart = false)
{
    public static VideoTrialDocument Empty => new(1, null, null, null, [], [], []);
}
public sealed record VideoTrialSnapshot(VideoTrialDocument Document, string StorageStatus, bool RecoveryRequired,
    bool AutomaticSuppressed, int? StorageHResult = null, string? StorageExceptionType = null)
{
    public static VideoTrialSnapshot Unavailable => new(VideoTrialDocument.Empty, "not-enabled", false, false);
    public bool AutomaticAllowed(VideoParameters desired, string? scope = null)
    {
        if (StorageStatus is not ("ready" or "not-enabled") || RecoveryRequired || AutomaticSuppressed) { return false; }
        if (Document.Active is { } active && active.Desired == desired && (scope is null || active.Scope == scope)) { return true; }
        VideoTrialAttempt? latest = scope is null ? Document.LastAttempt :
            Document.RecentAttempts.FirstOrDefault(a => a.Scope == scope && a.Desired == desired &&
                (a.State != "ended" || a.FirstFrameAt is not null));
        return latest is null || latest.Desired != desired || !latest.BlocksAutomatic;
    }
}

public interface IVideoTrialStore : IDisposable
{
    public VideoTrialSnapshot Snapshot { get; }
    public event EventHandler? Changed;
    public ValueTask InitializeAsync(CancellationToken token);
    public ValueTask<string> StageChangeAsync(VideoTrialContext context, VideoParameters previous, VideoParameters candidate, CancellationToken token);
    public ValueTask ClearChangeAsync(string id, CancellationToken token);
    public ValueTask<string> BeginAsync(VideoTrialContext context, VideoParameters desired, VideoParameters applied, CancellationToken token);
    public ValueTask FirstFrameAsync(string id, VideoFrameEvidence frame, CancellationToken token);
    public ValueTask StableAsync(string id, long submittedFrames, CancellationToken token);
    public ValueTask EndAsync(string id, bool failed, string reason, CancellationToken token, bool requiresManualRetry = false);
    public ValueTask AcknowledgeRecoveryAsync(CancellationToken token);
    public VideoRecoveryCandidate? FindRecovery(VideoTrialContext context);
    public VideoSuccessProfile? GetSuccess(VideoTrialContext context);
    public bool AutomaticAllowed(VideoTrialContext context, VideoParameters desired);
}

public sealed class VideoTrialException(string reasonCode, string message, Exception? inner = null) : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}
