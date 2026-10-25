namespace VRPhoneScreenOverlay.Contracts;

public sealed record ComponentFileEvidence(string File, string Status, long? Bytes,
    string ExpectedSha256, string? ActualSha256, int? HResult = null);

public sealed record ComponentRecoveryEvidence(int SchemaVersion, string AttemptId, string Release,
    string Location, DateTimeOffset StartedAt, string Phase, string ReasonCode,
    ComponentFileEvidence[] Files, string? ExceptionType = null, int? HResult = null,
    string? RollbackStatus = null, string? RollbackExceptionType = null, int? RollbackHResult = null);
