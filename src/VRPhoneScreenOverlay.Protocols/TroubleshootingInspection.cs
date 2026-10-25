namespace VRPhoneScreenOverlay.Protocols;

/// <summary>Fixed application operations; never accept executable text or arbitrary command arguments.</summary>
public static class TroubleshootingChecks
{
    public const string Connection = "connection_scan";
    public const string Runtime = "runtime_sample";
    public const string Reproduce = "reproduce_runtime";
    public const string Bindings = "binding_check";
    public const string ConnectionDetails = "connection_details";
    public const string Startup = "startup_check";
    public const string Files = "software_files";
    public const string Snapshot = "runtime_snapshot";
    public const string Logs = "recent_logs";
    public const int MaximumRounds = 3;
    public const int AgentMaximumRounds = 6;
    public const int AgentMaximumSourceSearches = 4;
    public const int AgentMaximumDecisionCalls = 6;
    public const int CurrentInspectionVersion = 6;
    public const int AgentDecisionSeconds = 180;
    public const int AgentTransportSeconds = 190;
    public const int AgentRequestSeconds = 195;
    public static TimeSpan DecisionTimeout(int version) => TimeSpan.FromSeconds(version >= 6 ? AgentDecisionSeconds : 55);
    public const int MaximumEvidencePerCheck = 9; // Runtime sample includes a separate, time-qualified permission observation.
    public static int RoundLimit(int version) => version >= 5 ? AgentMaximumRounds : MaximumRounds;

    public static bool Supported(string check) => check is Connection or Runtime or Reproduce or Bindings or ConnectionDetails or Startup or Files or Snapshot or Logs;
    public static bool ValidChecks(string[]? checks) => checks is { Length: <= 2 } &&
        checks.All(Supported) && checks.Distinct(StringComparer.Ordinal).Count() == checks.Length;

    public static string Label(string check) => check switch
    {
        Connection => "重新检查手机连接",
        Runtime => "观察画面、声音和控制",
        Reproduce => "采集故障复现过程",
        Bindings => "检查手柄绑定状态",
        ConnectionDetails => "分阶段检查手机连接",
        Startup => "检查浮窗启动条件",
        Files => "检查软件文件和保存目录",
        Snapshot => "读取当前运行状态和配置",
        Logs => "读取近期运行日志",
        _ => "未知检查",
    };

    public static bool ValidResult(TroubleshootingCheckResult? result) => result is not null && Supported(result.Check) &&
        result.Status is "completed" or "timeout" or "unavailable" or "skipped" or "failed" &&
        result.FinishedAt >= result.StartedAt && result.FinishedAt - result.StartedAt <= TimeSpan.FromMinutes(3) &&
        result.Summary is { Length: > 0 and <= 160 } && !result.Summary.Any(char.IsControl) &&
        result.Evidence is { Length: > 0 and <= MaximumEvidencePerCheck } && result.Evidence.All(e => e is not null &&
            e.Component is { Length: > 0 and <= 48 } && e.Component.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') &&
            e.Observation is { Length: > 0 and <= 1024 });
}

public sealed record TroubleshootingCheckResult(string Check, DateTimeOffset StartedAt, DateTimeOffset FinishedAt,
    string Status, string Summary, TroubleshootingEvidence[] Evidence);

public sealed record TroubleshootingInspectionRequest(int Round, TroubleshootingCheckResult[] Results);

public sealed record TroubleshootingInspectionRound(int Round, TroubleshootingCheckResult[] Results,
    TroubleshootingPlan? Conclusion, TroubleshootingAnalysisMetadata? Analysis);

public sealed record TroubleshootingAvailability(bool Allowed, string? ReasonCode = null, int RetryAfterSeconds = 0,
    bool TestUnlimited = false);
