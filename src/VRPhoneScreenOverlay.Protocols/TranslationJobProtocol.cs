namespace VRPhoneScreenOverlay.Protocols;

/// <summary>Metadata only. Image bytes travel from the client to the upload provider.</summary>
public sealed record TranslationJobCreateRequest(
    int Version, Guid RequestId, int Mode, string MediaType, long Size,
    string Md5, string Sha256, DateTimeOffset CapturedAt);

/// <summary>Provider-scoped upload capability; never contains an account credential.</summary>
public sealed record TranslationUploadPart(int Number, long Offset, int Length, string Url);

public sealed record TranslationJobCreated(
    Guid JobId, DateTimeOffset ExpiresAt, IReadOnlyList<TranslationUploadPart> Parts);

public enum TranslationJobPhase
{
    Created,
    Uploading,
    Uploaded,
    Recognizing,
    Completed,
    Failed,
    Cancelled,
    Expired,
}

/// <summary>Cleanup has its own state so deleting an image cannot erase the result status.</summary>
public enum TranslationCleanupPhase { NotRequired, Pending, Deleted, NoUploadIssued }

public sealed record TranslationJobResult(string Title, string Text);

public sealed record TranslationJobStatus(
    Guid JobId, TranslationJobPhase Phase, TranslationCleanupPhase Cleanup,
    TranslationJobResult? Result, string? FailureCode, bool SupportsResultWait = false);

/// <summary>Missing usage remains unknown, including interrupted or cancelled provider calls.</summary>
public sealed record TranslationModelUsage(
    long? InputTokens, long? OutputTokens, long? CachedInputTokens);

/// <summary>Private administrative aggregates by installation, never a claim of unique people.</summary>
public sealed record TranslationDailyUsage(
    string Installation, DateOnly Day, long Submitted, long Succeeded, long Failed,
    long Cancelled, long CapacityRejected, long ModelCalls, long UnknownUsageCalls,
    long InputTokens, long OutputTokens, long CachedInputTokens, long UploadedBytes);
