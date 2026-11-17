namespace VRPhoneScreenOverlay.Contracts;

public enum TranslationMode { AnswerAndTranslate, Translate, ExplainScene }
public enum TranslationApiSource { BuiltIn, Personal }

public enum TranslationPhase { Idle, CountingDown, Capturing, Requesting, Saving, Completed, Failed, PreparingUpload, Uploading, ConfirmingUpload, Translating }

/// <summary>A single bounded, encoded frame. Ownership transfers to the requesting operation.</summary>
public sealed record TranslationFrame(byte[] Bytes, string MediaType, DateTimeOffset CapturedAt);

/// <summary>Persistable result; never contains credentials or image bytes.</summary>
public sealed record TranslationRecord(
    Guid Id,
    DateTimeOffset CreatedAt,
    TranslationMode Mode,
    string Title,
    string Text,
    string? FailureCode = null)
{
    public string? PersistenceFailureCode { get; init; }
}

public sealed record TranslationProgress(TranslationPhase Phase, int SecondsRemaining = 0, string? FailureCode = null);

public sealed record TranslationPreferences(bool ShutterSound = true, int HistoryLimit = 100, int CaptureRangePercent = 65)
{
    public TranslationApiSource ApiSource { get; init; } = TranslationApiSource.BuiltIn;
    public const int MaximumHistoryLimit = 1000;
    public bool IsValid => HistoryLimit is >= 1 and <= MaximumHistoryLimit && CaptureRangePercent is >= 30 and <= 100 && Enum.IsDefined(ApiSource);
}

public interface ITranslationFrameSource
{
    public Task<TranslationFrame> CaptureAsync(CancellationToken cancellationToken);
}

/// <summary>Prepare immediately, acquire only after trigger, notify before encoding/cleanup.</summary>
public interface IArmedTranslationFrameSource : ITranslationFrameSource
{
    public Task<TranslationFrame> CaptureAsync(Task trigger, Action captured, CancellationToken cancellationToken);
}

public interface ITranslationModelClient
{
    public Task<TranslationRecord> AnalyzeAsync(TranslationMode mode, TranslationFrame frame, CancellationToken cancellationToken);
}

/// <summary>Optional request-scoped progress; no global callback or cross-request state.</summary>
public interface IProgressTranslationModelClient : ITranslationModelClient
{
    public Task<TranslationRecord> AnalyzeAsync(TranslationMode mode, TranslationFrame frame,
        IProgress<TranslationProgress>? progress, CancellationToken cancellationToken);
}

public interface ITranslationHistory
{
    public Task<IReadOnlyList<TranslationRecord>> LoadAsync(CancellationToken cancellationToken);
    public Task SaveAsync(TranslationRecord record, int retentionLimit, CancellationToken cancellationToken);
}
