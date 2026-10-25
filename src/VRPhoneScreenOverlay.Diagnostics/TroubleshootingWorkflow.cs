using System.Text.Json;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics;

public interface ITemporaryRepairAttempt
{
    public Task<TroubleshootingOutcome> ExecuteAsync(TroubleshootingPlan plan, CancellationToken cancellationToken);
}

public interface ITemporaryRepairExecutor
{
    public ITemporaryRepairAttempt Capture();
}

public sealed record TroubleshootingRunResult(TroubleshootingCase Case, bool ResultPendingUpload);

/// <summary>One user-initiated run. Durable outbox holds at most 100 records, rejects overflow and never replays repairs.</summary>
public sealed partial class TroubleshootingWorkflow : IDisposable
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly ITroubleshootingClient _client;
    private readonly ITemporaryRepairExecutor _repair;
    private readonly string _outbox;
    private readonly ILocalTroubleshootingInspector? _inspector;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TroubleshootingWorkflow(ITroubleshootingClient client, ITemporaryRepairExecutor repair, string outbox,
        ILocalTroubleshootingInspector? inspector = null)
    {
        _client = client;
        _repair = repair;
        _outbox = outbox;
        _inspector = inspector;
    }

    public Task<TroubleshootingAvailability> AvailabilityAsync(CancellationToken token) => _client is ILiveTroubleshootingClient live
        ? live.AvailabilityAsync(token) : Task.FromResult(new TroubleshootingAvailability(true));

    public async Task<TroubleshootingRunResult> RunAsync(string uploadId, IProgress<string>? progress, CancellationToken cancellationToken,
        Func<CancellationToken, Task<bool>>? prepareReproduction = null,
        Func<bool, CancellationToken, Task<bool>>? repairControls = null)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) { throw new InvalidOperationException("AI_BUSY"); }
        string id = Guid.NewGuid().ToString("N");
        TroubleshootingCase? item = null;
        try
        {
            Directory.CreateDirectory(_outbox);
            if (Directory.EnumerateFiles(_outbox, "*.json").Take(100).Count() >= 100)
            { throw new InvalidDataException("AI_OUTBOX_FULL"); }
            ITemporaryRepairAttempt attempt = _repair.Capture();
            await SaveAsync(new(id, null, null), cancellationToken).ConfigureAwait(false);
            progress?.Report("已提交诊断信息，正在等待DeepSeek返回分析结果…");
            if (_inspector is not null)
            {
                ILiveTroubleshootingClient live = _client as ILiveTroubleshootingClient ?? throw new InvalidOperationException("AI_LIVE_UNAVAILABLE");
                item = await live.BeginInspectionAsync(id, uploadId, cancellationToken).ConfigureAwait(false);
                while (item.State == TroubleshootingStates.PlanReady && item.Plan is { Checks.Length: > 0 } plan)
                {
                    if (item.InspectionRound >= TroubleshootingChecks.RoundLimit(item.InspectionVersion) || !TroubleshootingChecks.ValidChecks(plan.Checks))
                    { throw new InvalidDataException("AI_CHECK_LIMIT"); }
                    progress?.Report(plan.Summary);
                    List<TroubleshootingCheckResult> checks = new(2);
                    foreach (string check in plan.Checks)
                    {
                        TroubleshootingCheckResult result = await _inspector.InspectAsync(check, progress, prepareReproduction, cancellationToken).ConfigureAwait(false);
                        if (result.Check != check || !TroubleshootingChecks.ValidResult(result)) { throw new InvalidDataException("AI_LOCAL_RESULT_INVALID"); }
                        checks.Add(result);
                        progress?.Report(result.Summary);
                    }
                    int round = item.InspectionRound + 1;
                    await SaveInvestigationAsync(item with { InspectionHistory = [.. item.InspectionHistory, new(round, checks.ToArray(), null, null)] }, cancellationToken).ConfigureAwait(false);
                    // Capture the same local session just observed, before waiting for the remote decision.
                    attempt = _repair.Capture();
                    progress?.Report($"第 {round} 轮现场检查已完成，DeepSeek正在分析实际结果…");
                    item = await live.InspectAsync(id, new(round, checks.ToArray()), cancellationToken).ConfigureAwait(false);
                    await SaveInvestigationAsync(item, cancellationToken).ConfigureAwait(false);
                }
            }
            else { item = await _client.AnalyzeAsync(id, uploadId, cancellationToken).ConfigureAwait(false); }
            if (item.State != TroubleshootingStates.PlanReady || item.Plan is null)
            {
                File.Delete(PathFor(id));
                return new(item, false);
            }
            bool shortReply = item.Plan.RequestKind != TroubleshootingRequestKinds.Diagnostic;
            if (item.InspectionVersion >= 5 && item.Plan.Action != TroubleshootingActions.None)
            { throw new InvalidDataException("AI_AGENT_ACTION_FORBIDDEN"); }
            progress?.Report((shortReply ? "回复：" : item.Analysis?.DecisionAdjusted == true ? "排查结论：" : "DeepSeek分析：") + item.Plan.Summary);
            progress?.Report(shortReply ? "正在保存回复…" : item.Plan.Action switch
            {
                TroubleshootingActions.RestartMedia => "正在尝试重新建立当前媒体会话，画面可能短暂中断…",
                TroubleshootingActions.ReduceVideoLoad => "正在尝试临时降低视频负载，并检查画面提交是否恢复…",
                _ => "本次提供处理建议，正在整理诊断结果…",
            });
            TroubleshootingOutcome outcome;
            bool needsLock = item.Plan.Action != TroubleshootingActions.None;
            bool acquired = false;
            try
            {
                acquired = !needsLock || repairControls is null || await repairControls(true, cancellationToken).ConfigureAwait(false);
                if (!acquired) { outcome = new(Guid.NewGuid().ToString("N"), "skipped", "unknown", "AI_USER_OPERATION_BUSY", "not_needed", []); }
                else
                {
                    if (shortReply)
                    { outcome = new(Guid.NewGuid().ToString("N"), "skipped", "unknown", "AI_SHORT_REPLY", "not_needed", []); }
                    else
                    {
                        // An uncertain authorization must never cause a second execution.
                        await _client.BeginExecutionAsync(id, cancellationToken).ConfigureAwait(false);
                        outcome = await attempt.ExecuteAsync(item.Plan, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (needsLock && acquired && repairControls is not null)
                { await repairControls(false, CancellationToken.None).ConfigureAwait(false); }
            }
            using CancellationTokenSource finalization = new(TimeSpan.FromSeconds(25));
            await SaveAsync(new(id, outcome, null), finalization.Token).ConfigureAwait(false);
            if (_inspector is not null && outcome.Execution == "executed" && !cancellationToken.IsCancellationRequested)
            {
                progress?.Report("正在重新观察修复后的画面、声音和控制，与修复前对比…");
                try
                {
                    TroubleshootingCheckResult after = await _inspector.InspectAsync(TroubleshootingChecks.Runtime, progress, null, finalization.Token).ConfigureAwait(false);
                    outcome = outcome with { Checks = [after] };
                }
                catch (OperationCanceledException)
                { return new(item with { Outcome = outcome, State = TroubleshootingStates.Completed }, true); }
            }
            await SaveAsync(new(id, outcome, null), finalization.Token).ConfigureAwait(false);
            progress?.Report("已记录执行结果，正在上传诊断与修复结果…");
            if (_inspector is not null)
            { await SaveInvestigationAsync(item with { Outcome = outcome, State = outcome.Execution == "cancelled" ? TroubleshootingStates.Cancelled : TroubleshootingStates.Completed }, finalization.Token).ConfigureAwait(false); }
            try
            {
                item = await _client.CompleteAsync(id, outcome, finalization.Token).ConfigureAwait(false);
                if (_inspector is not null) { await SaveInvestigationAsync(item, finalization.Token).ConfigureAwait(false); }
                File.Delete(PathFor(id));
                return new(item, false);
            }
            catch (Exception exception) when (IsRecoverable(exception))
            { return new(item with { Outcome = outcome, State = outcome.Execution == "cancelled" ? TroubleshootingStates.Cancelled : TroubleshootingStates.Completed }, true); }
        }
        catch (Exception exception) when (IsRecoverable(exception) || exception is InvalidOperationException)
        {
            await CancelIntentAsync(id).ConfigureAwait(false);
            throw;
        }
        finally { _gate.Release(); }
    }

    private async Task CancelIntentAsync(string id)
    {
        using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(8));
        try
        {
            await _client.CancelAsync(id, cleanup.Token).ConfigureAwait(false);
            // Only remove intent-only records. An already saved outcome must remain available for replay.
            OutboxRecord? record = await ReadAsync(PathFor(id), cleanup.Token).ConfigureAwait(false);
            if (record?.Outcome is null && record?.Feedback is null) { File.Delete(PathFor(id)); }
        }
        catch (Network.NetworkException exception) when (exception.StatusCode == 404)
        {
            OutboxRecord? record = await ReadAsync(PathFor(id), cleanup.Token).ConfigureAwait(false);
            if (record?.Outcome is null && record?.Feedback is null) { File.Delete(PathFor(id)); }
        }
        catch (Exception failure) when (IsRecoverable(failure)) { }
    }

    public async Task<TroubleshootingCase[]> HistoryAsync(CancellationToken cancellationToken)
    {
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return await _client.HistoryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task FeedbackAsync(string id, string value, CancellationToken cancellationToken)
    {
        if (!TroubleshootingProtocol.ValidFeedback(value)) { throw new InvalidDataException("AI_FEEDBACK_INVALID"); }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_outbox);
            OutboxRecord? previous = await ReadAsync(PathFor(id), cancellationToken).ConfigureAwait(false);
            if (previous is null && Directory.EnumerateFiles(_outbox, "*.json").Take(100).Count() >= 100)
            { throw new InvalidDataException("AI_OUTBOX_FULL"); }
            await SaveAsync(new(id, previous?.Outcome, value), cancellationToken).ConfigureAwait(false);
            if (previous?.Outcome is not null) { await _client.CompleteAsync(id, previous.Outcome, cancellationToken).ConfigureAwait(false); }
            await _client.FeedbackAsync(id, value, cancellationToken).ConfigureAwait(false);
            File.Delete(PathFor(id));
        }
        finally { _gate.Release(); }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) { return; }
        try
        {
            if (!Directory.Exists(_outbox)) { return; }
            // A page refresh retries at most ten items; no background retry loop or device execution.
            foreach (string path in Directory.EnumerateFiles(_outbox, "*.json").Order(StringComparer.Ordinal).Take(10))
            {
                cancellationToken.ThrowIfCancellationRequested();
                OutboxRecord? record = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
                if (record is null) { continue; }
                try
                {
                    if (record.Outcome is not null) { await _client.CompleteAsync(record.Id, record.Outcome, cancellationToken).ConfigureAwait(false); }
                    else if (record.Feedback is null) { await _client.CancelAsync(record.Id, cancellationToken).ConfigureAwait(false); }
                    if (record.Feedback is not null) { await _client.FeedbackAsync(record.Id, record.Feedback, cancellationToken).ConfigureAwait(false); }
                    File.Delete(path);
                }
                catch (Network.NetworkException exception) when (exception.StatusCode == 404 && record.Outcome is null && record.Feedback is null)
                { File.Delete(path); } // The cancelled request never reached the server; no paid call to repeat.
                catch (Exception exception) when (IsRecoverable(exception) && !cancellationToken.IsCancellationRequested)
                { break; } // Leave records intact, but allow the independent history read to proceed.
            }
        }
        finally { _gate.Release(); }
    }

    private string PathFor(string id)
    {
        if (!TroubleshootingProtocol.ValidRequestId(id)) { throw new InvalidDataException("AI_ID_INVALID"); }
        return Path.Combine(_outbox, id + ".json");
    }

    private async Task SaveAsync(OutboxRecord record, CancellationToken cancellationToken)
    {
        string path = PathFor(record.Id);
        string temporary = path + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record, _json);
        if (bytes.Length > 128 * 1024) { throw new InvalidDataException("AI_OUTBOX_RECORD_LIMIT"); }
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static async Task<OutboxRecord?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        if (!file.Exists) { return null; }
        if (file.Length > 128 * 1024 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        { throw new InvalidDataException("AI_OUTBOX_INVALID"); }
        OutboxRecord? record = JsonSerializer.Deserialize<OutboxRecord>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), _json);
        if (record is null || record.Id != Path.GetFileNameWithoutExtension(path)) { throw new InvalidDataException("AI_OUTBOX_INVALID"); }
        return record;
    }

    internal static bool IsRecoverable(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or
        OperationCanceledException or JsonException or Network.NetworkException or HttpRequestException;

    private sealed record OutboxRecord(string Id, TroubleshootingOutcome? Outcome, string? Feedback);

    public void Dispose() => _gate.Dispose();
}
