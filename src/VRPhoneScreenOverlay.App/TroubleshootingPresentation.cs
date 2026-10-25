using System.Globalization;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.App;

internal static class TroubleshootingPresentation
{
    internal static string Unavailable(TroubleshootingAvailability availability) => availability.ReasonCode switch
    {
        "AI_CLIENT_DAILY_LIMIT" => "今天的诊断次数已用完，尚未上传；仍可使用普通上传。",
        "AI_SERVICE_DAILY_LIMIT" => "诊断服务今天的额度已用完，尚未上传；仍可使用普通上传。",
        "AI_COOLDOWN" => $"请等待约 {availability.RetryAfterSeconds} 秒后再诊断，尚未上传。",
        _ => "诊断服务暂不可用，尚未上传；仍可使用普通上传。",
    };
    internal static string Failure(string? reason) => reason switch
    {
        "AI_CLIENT_DAILY_LIMIT" => "今天的诊断次数已用完，本次未调用AI。现场记录已保留。",
        "AI_QUOTA_EXCEEDED" => "诊断额度不足，本次未调用AI。现场记录已保留。",
        "AI_SERVICE_DAILY_LIMIT" => "诊断服务今天的额度已用完，本次未调用AI。请稍后再试。",
        "AI_COOLDOWN" => "两次诊断间隔过短，请稍等一分钟再试。本次未调用AI。",
        "AI_BUSY" => "诊断服务正忙，请稍后重试。",
        "AI_AGENT_LIMIT" => "本次调查达到上限，尚未完成；已有发现和检查记录已保留。",
        _ => "本次诊断未完成，检查记录已保留，请稍后重试。",
    };
    internal static string Preview(TroubleshootingCase item)
    {
        string description = Description(item);
        StringInfo text = new(description.ReplaceLineEndings(" "));
        return text.LengthInTextElements > 10 ? text.SubstringByTextElements(0, 10) + "…" : text.String;
    }

    private static string Description(TroubleshootingCase item) => item.UserDescription is null ? "未记录故障描述" :
        string.IsNullOrWhiteSpace(item.UserDescription) ? "未填写故障描述" : item.UserDescription.Trim();

    internal static string Result(TroubleshootingCase item)
    {
        if (item.State == TroubleshootingStates.Completed && item.Plan?.RequestKind is { } kind && kind != TroubleshootingRequestKinds.Diagnostic)
        { return kind == TroubleshootingRequestKinds.Test ? "测试已响应" : "已简短回复"; }
        if (item.Feedback == "fixed") { return "已修复（用户确认）"; }
        if (item.Feedback == "not_fixed") { return "没修好（用户反馈）"; }
        if (item.State == TroubleshootingStates.Cancelled) { return "已取消"; }
        if (item.State == TroubleshootingStates.Interrupted) { return "已中断"; }
        if (item.State == TroubleshootingStates.Failed)
        { return item.ReasonCode is "AI_CLIENT_DAILY_LIMIT" or "AI_SERVICE_DAILY_LIMIT" or "AI_QUOTA_EXCEEDED" ? "额度不足" : "诊断未完成"; }
        if (item.Outcome is { } outcome)
        {
            return outcome.Execution switch
            {
                "skipped" => outcome.ReasonCode == "AI_GUIDANCE_ONLY" ? "仅提供建议" : "未执行修复",
                "failed" => "修复未成功",
                "cancelled" => "已取消",
                _ => outcome.Verification == "not_recovered" ? "尚未恢复" : "已尝试，待确认",
            };
        }
        return item.State switch
        {
            TroubleshootingStates.Created or TroubleshootingStates.Analyzing => "分析中",
            TroubleshootingStates.PlanReady => "分析已返回",
            TroubleshootingStates.Executing => "执行中",
            TroubleshootingStates.Completed => "诊断已完成",
            _ => "诊断未完成",
        };
    }

    internal static string Details(TroubleshootingCase item)
    {
        if (item.Plan is { RequestKind: not TroubleshootingRequestKinds.Diagnostic } reply)
        { return $"你的描述：{Description(item)}{Environment.NewLine}{Environment.NewLine}{reply.Summary}"; }
        string action = item.Plan?.Action switch
        {
            TroubleshootingActions.RestartMedia => "重新连接当前手机画面与控制",
            TroubleshootingActions.ReduceVideoLoad => "临时降低视频负载",
            TroubleshootingActions.None => "提供手动处理建议，不执行自动修复",
            _ => "尚无可用方案",
        };
        string execution = item.Outcome?.Execution switch
        {
            "executed" => "已执行临时操作，实际效果仍需确认",
            "skipped" => "未执行自动修复",
            "failed" => "临时操作未成功",
            "cancelled" => "临时操作已取消",
            _ => "尚无执行结果",
        };
        string text = $"时间：{item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)}" +
            $"{Environment.NewLine}结果：{Result(item)}{Environment.NewLine}故障描述：{Description(item)}{Environment.NewLine}{Environment.NewLine}" +
            $"{item.Plan?.Summary ?? "本次未取得完整分析结果。"}{Environment.NewLine}{Environment.NewLine}" +
            $"方案：{action}{Environment.NewLine}执行：{execution}";
        if (item.State == TroubleshootingStates.Failed) { text = Failure(item.ReasonCode) + Environment.NewLine + text; }
        if (item.Outcome?.Rollback == "restored") { text += Environment.NewLine + "已恢复原运行参数。"; }
        else if (item.Outcome?.Rollback == "failed") { text += Environment.NewLine + "原运行参数恢复未完成，请手动重新打开浮窗。"; }
        foreach (TroubleshootingInspectionRound round in item.InspectionHistory)
        {
            text += $"{Environment.NewLine}{Environment.NewLine}第 {round.Round} 轮现场检查：";
            foreach (TroubleshootingCheckResult check in round.Results)
            { text += $"{Environment.NewLine}{check.StartedAt.ToLocalTime():HH:mm:ss} {TroubleshootingChecks.Label(check.Check)}：{check.Summary}"; }
            if (round.Conclusion is not null) { text += Environment.NewLine + (round.Analysis?.DecisionAdjusted == true ? "后续安排：" : "分析：") + round.Conclusion.Summary; }
        }
        foreach (TroubleshootingCheckResult check in item.Outcome?.Checks ?? [])
        { text += Environment.NewLine + "修复后复核：" + check.Summary; }
        return text;
    }
}
