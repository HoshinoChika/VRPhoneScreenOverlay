namespace VRPhoneScreenOverlay.Contracts;

public sealed record ServerOutputError(string Category, long ObservedLines, DateTimeOffset FirstAt, DateTimeOffset LastAt)
{
    public IReadOnlyList<string> KnownExceptionTypes { get; init; } = [];
}

/// <summary>Counts observed server output, not acknowledged input commands. Contains no raw output.</summary>
public sealed record ServerOutputDiagnostic(long Revision, DateTimeOffset CapturedAt,
    long TotalCharacters, long TotalLines, long StandardOutputLines, long StandardErrorLines,
    long TruncatedLines, long OmittedTailLines, int RetainedTailBytes, int TailByteCapacity,
    int LineCharacterCapacity, bool StandardOutputCompleted, bool StandardErrorCompleted,
    string? StandardOutputFailureType, string? StandardErrorFailureType, IReadOnlyList<ServerOutputError> Errors)
{
    public bool InputPermissionRefusalObserved => Errors.Any(error => error.Category == "input-permission-denied");
    public string? ReportingFailureType { get; init; }
    public string? SessionId { get; init; }
}
