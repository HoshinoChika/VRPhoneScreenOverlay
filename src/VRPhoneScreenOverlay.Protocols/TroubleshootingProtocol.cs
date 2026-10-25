namespace VRPhoneScreenOverlay.Protocols;

/// <summary>Wire values are fixed identifiers, not model-provided commands.</summary>
public static class TroubleshootingActions
{
    public const string None = "none";
    public const string RestartMedia = "restart_current_media";
    public const string ReduceVideoLoad = "reduce_video_load";

    public static bool IsSupported(string action) => action is None or RestartMedia or ReduceVideoLoad;
}

public static class TroubleshootingStates
{
    public const string Created = "created";
    public const string Analyzing = "analyzing";
    public const string PlanReady = "plan_ready";
    public const string Executing = "executing";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Interrupted = "interrupted";
}

public sealed record TroubleshootingCreateRequest(int SchemaVersion, string RequestId, string UploadId)
{
    public bool LiveInspection { get; init; }
    public int InspectionVersion { get; init; } = 1;
}

public sealed record TroubleshootingEvidence(string Id, string Component, string Observation);

public sealed record TroubleshootingPlan(string Summary, string[] EvidenceIds, string Action)
{
    public string[] Checks { get; init; } = [];
    public string RequestKind { get; init; } = TroubleshootingRequestKinds.Diagnostic;
}

public static class TroubleshootingRequestKinds
{
    public const string Diagnostic = "diagnostic";
    public const string Test = "test";
    public const string OutOfScope = "out_of_scope";
}

/// <summary>Execution and observation are distinct from the user's subjective acceptance.</summary>
public sealed record TroubleshootingOutcome(string OperationId, string Execution, string Verification,
    string ReasonCode, string Rollback, TroubleshootingEvidence[] Observations)
{
    public TroubleshootingCheckResult[] Checks { get; init; } = [];
}

public sealed record TroubleshootingFeedback(string Value);

public sealed record TroubleshootingAnalysisMetadata(string Model, int InputTokens, int OutputTokens, int PromptVersion)
{
    public bool SummarySimplified { get; init; }
    public TroubleshootingPlan? ProposedPlan { get; init; }
    public bool DecisionAdjusted { get; init; }
}

public sealed record TroubleshootingCase(string Id, string UploadId, string AppVersion,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string State,
    TroubleshootingEvidence[] Evidence, TroubleshootingPlan? Plan, TroubleshootingOutcome? Outcome,
    string? Feedback, string? ReasonCode)
{
    public string? FailureStage { get; init; }
    public TroubleshootingAnalysisMetadata? Analysis { get; init; }
    public TroubleshootingAnalysisMetadata? IntakeAnalysis { get; init; }
    public string? UserDescription { get; init; }
    public bool LiveInspection { get; init; }
    public int InspectionVersion { get; init; } = 1;
    public int InspectionRound { get; init; }
    public TroubleshootingInspectionRound[] InspectionHistory { get; init; } = [];
}

public static class TroubleshootingProtocol
{
    public const int SchemaVersion = 1;
    public const int MaximumHistory = 20;
    public const int MaximumEvidence = 64;
    public const int MaximumObservationLength = 1024;

    public static bool ValidRequestId(string? value) =>
        Guid.TryParseExact(value, "N", out Guid id) && id != Guid.Empty;

    public static bool ValidFeedback(string? value) => value is "fixed" or "not_fixed";

    public static bool ValidPlan(TroubleshootingPlan? plan, IReadOnlyList<TroubleshootingEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return plan is not null && plan.Summary is { Length: > 0 and <= 2000 } &&
            !plan.Summary.Any(char.IsControl) && TroubleshootingActions.IsSupported(plan.Action) &&
            plan.RequestKind is TroubleshootingRequestKinds.Diagnostic or TroubleshootingRequestKinds.Test or TroubleshootingRequestKinds.OutOfScope &&
            (plan.RequestKind == TroubleshootingRequestKinds.Diagnostic || (plan.Action == TroubleshootingActions.None && plan.Checks is { Length: 0 })) &&
            TroubleshootingChecks.ValidChecks(plan.Checks) && (plan.Checks.Length == 0 || plan.Action == TroubleshootingActions.None) &&
            plan.EvidenceIds is { Length: <= MaximumEvidence } &&
            (plan.Action == TroubleshootingActions.None || plan.EvidenceIds.Length > 0) &&
            plan.EvidenceIds.Distinct(StringComparer.Ordinal).Count() == plan.EvidenceIds.Length &&
            plan.EvidenceIds.All(id => evidence.Any(item => item.Id == id));
    }
}
