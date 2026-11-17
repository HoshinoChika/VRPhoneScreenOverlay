using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal sealed class TranslationPanelController : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private string? _failureCode;
    public string? FailureCode => Volatile.Read(ref _failureCode);

    public TranslationPanelController(OpenVrTranslationChannel channel, TranslationWorkflow workflow,
        ITranslationHistory history, TranslationConfigurationStore configuration, Action shutter, IIncidentRecorder? incident = null, Action? countdown = null)
    {
        _worker = Task.Run(() => PanelWorker.RunAsync(
            () => RunAsync(channel, workflow, history, configuration, shutter, countdown, _stop.Token),
            "TRANSLATION_PANEL_FAILED", code =>
            {
                Volatile.Write(ref _failureCode, code);
                channel.SetPressed(null);
                channel.Publish(TranslationPanelRenderer.Render([], new HashSet<Guid>(),
                    "翻译面板已停止，请重启软件后重试。", true, 0, long.MaxValue));
            }, incident, _stop.Token));
    }

    private static async Task RunAsync(OpenVrTranslationChannel channel, TranslationWorkflow workflow,
        ITranslationHistory history, TranslationConfigurationStore configuration, Action shutter, Action? countdown, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        token = lifetime.Token;
        var records = new List<TranslationRecord>();
        var expanded = new HashSet<Guid>();
        string status = "点击功能后 3 秒截图。请靠近并看向目标，保持内容居中、清晰、无遮挡。";
        int scroll = 0;
        long revision = 0;
        TranslationPanelHit? paintedPress = null;
        bool paintedSound = configuration.Preferences.ShutterSound;
        TranslationProgress? progressValue = null;
        TranslationProgress? paintedProgress = null;
        Task<TranslationRecord?>? active = null;
        TranslationMode activeMode = TranslationMode.Translate;
        try { records.AddRange((await history.LoadAsync(token)).Take(100)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { status = "历史记录暂时无法读取。"; }
        if (records.Count > 0) { expanded.Add(records[0].Id); }
        Publish();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(33));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                bool changed = channel.Pressed != paintedPress || configuration.Preferences.ShutterSound != paintedSound;
                TranslationProgress? nextProgress = Volatile.Read(ref progressValue);
                if (nextProgress is not null && nextProgress != paintedProgress)
                {
                    paintedProgress = nextProgress;
                    status = nextProgress.Phase switch
                    {
                        TranslationPhase.CountingDown => $"{nextProgress.SecondsRemaining} 秒后截图，请靠近并看向目标，保持居中、清晰、无遮挡。",
                        TranslationPhase.Capturing => "正在截图…",
                        TranslationPhase.Requesting => "正在识别，请稍候…",
                        TranslationPhase.PreparingUpload => "正在准备上传…",
                        TranslationPhase.Uploading => "图片上传中…",
                        TranslationPhase.ConfirmingUpload => "正在确认上传…",
                        TranslationPhase.Translating => "翻译处理中，请稍候…",
                        TranslationPhase.Saving => "正在保存结果…",
                        _ => "识别完成。",
                    };
                    changed = true;
                }
                if (active?.IsCompleted == true)
                {
                    TranslationRecord? result;
                    try { result = await active; }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (Exception exception)
                    {
                        result = new(Guid.NewGuid(), DateTimeOffset.UtcNow, activeMode, "识别未完成",
                            TranslationFailureMessages.Describe(exception) + "\n本次请求不会自动重试。", "TRANSLATION_FAILED");
                        try { await history.SaveAsync(result, 100, token); }
                        catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
                        { result = result with { PersistenceFailureCode = "TRANSLATION_HISTORY_WRITE_FAILED" }; }
                    }
                    active = null;
                    Volatile.Write(ref progressValue, null); paintedProgress = null;
                    if (result is not null)
                    {
                        records.Insert(0, result);
                        if (records.Count > 100) { records.RemoveRange(100, records.Count - 100); }
                        expanded.Clear(); expanded.Add(result.Id); scroll = 0;
                        status = result.PersistenceFailureCode is not null ? "结果已显示，但未能保存到软件目录。" :
                            result.FailureCode is null ? "识别完成。" : "识别未完成，可重新尝试。";
                    }
                    changed = true;
                }
                int requestedScroll = channel.TakeScroll();
                if (requestedScroll >= 0) { scroll = requestedScroll; changed = true; }
                TranslationPanelHit? command = channel.Take();
                if (command is not null)
                {
                    changed = true;
                    switch (command.Action)
                    {
                        case TranslationPanelAction.SoundToggle:
                            try { await configuration.SavePreferencesAsync(configuration.Preferences with { ShutterSound = !configuration.Preferences.ShutterSound }, token); }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { status = "截图声音设置保存失败，请重试。"; }
                            break;
                        case TranslationPanelAction.ScrollUp: scroll = Math.Max(0, scroll - 480); break;
                        case TranslationPanelAction.ScrollDown: scroll = Math.Min(channel.Frame?.MaximumScroll ?? 0, scroll + 480); break;
                        case TranslationPanelAction.ToggleRecord:
                            if (command.RecordIndex >= 0 && command.RecordIndex < records.Count && !expanded.Add(records[command.RecordIndex].Id))
                            { expanded.Remove(records[command.RecordIndex].Id); }
                            break;
                        default:
                            TranslationMode? mode = command.Action switch
                            {
                                TranslationPanelAction.Answer => TranslationMode.AnswerAndTranslate,
                                TranslationPanelAction.Translate => TranslationMode.Translate,
                                TranslationPanelAction.Explain => TranslationMode.ExplainScene,
                                _ => null,
                            };
                            if (mode is null || active is not null) { break; }
                            if (string.IsNullOrWhiteSpace(configuration.Key)) { status = "请先在 PC 设置 → 翻译 → DeepSeek API 中配置密钥。"; break; }
                            activeMode = mode.Value;
                            active = workflow.RunAsync(mode.Value, configuration.Preferences,
                                new InlineProgress(value => Volatile.Write(ref progressValue, value)), shutter, token,
                                () => configuration.Preferences.ShutterSound, countdown);
                            break;
                    }
                }
                if (changed) { Publish(); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch { await lifetime.CancelAsync(); throw; }
        finally
        {
            if (active is not null)
            {
                try { await active; }
                catch (Exception) { /* Shutdown observes the cancelled/failed request without publishing new UI. */ }
            }
        }
        void Publish()
        {
            paintedPress = channel.Pressed; paintedSound = configuration.Preferences.ShutterSound;
            TranslationPanelFrame frame = TranslationPanelRenderer.Render(records, expanded, status, active is not null, scroll, ++revision,
                active is not null ? null : paintedPress?.Action, paintedSound);
            scroll = frame.Scroll;
            channel.Publish(frame);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try { await _worker; }
        finally { _stop.Dispose(); }
    }

    private sealed class InlineProgress(Action<TranslationProgress> update) : IProgress<TranslationProgress>
    {
        public void Report(TranslationProgress value) => update(value);
    }
}
