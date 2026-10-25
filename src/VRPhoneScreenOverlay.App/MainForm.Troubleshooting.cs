using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private TroubleshootingWorkflow? _troubleshooting;
    private string? _aiUploadId;
    private CancellationTokenSource? _aiCancellation;
    private bool _aiReturnRequested;
    private TaskCompletionSource<bool>? _aiReproduction;

    private void InitializeTroubleshooting(TroubleshootingWorkflow? workflow)
    {
        _troubleshooting = workflow;
        TroubleshootingPageView page = _aboutPage.TroubleshootingPage;
        page.CancelButton.Click += OnAiCancelClicked;
        page.RefreshButton.Click += OnAiRefreshClicked;
        page.FeedbackRequested += OnAiFeedbackRequested;
        page.ReproduceButton.Click += (_, _) => _aiReproduction?.TrySetResult(true);
        page.SkipReproductionButton.Click += (_, _) => _aiReproduction?.TrySetResult(false);
        _aboutPage.DeepSeekDiagnosticsButton.Click += OnDeepSeekDiagnosticsClicked;
        _aboutPage.ViewAiHistoryButton.Click += OnViewAiHistoryClicked;
    }

    private async void OnDeepSeekDiagnosticsClicked(object? sender, EventArgs args) =>
        await UploadDiagnosticsAsync(startAi: true);

    private async void OnViewAiHistoryClicked(object? sender, EventArgs args)
    {
        if (_aboutOperationInProgress) { return; }
        _aboutPage.ShowTroubleshooting();
        _aboutPage.TroubleshootingPage.SetBusy(false);
        _aboutPage.TroubleshootingPage.PrepareHistoryView();
        _aboutPage.TroubleshootingPage.StatusLabel.Text = _troubleshooting is null
            ? "暂时无法读取历史记录。" : "选择一条历史记录查看完整信息。";
        await RunAboutOperationAsync(RefreshAiHistoryAsync, _aboutPage.TroubleshootingPage.StatusLabel);
    }

    private async Task RunAiDiagnosisAsync()
    {
        if (_troubleshooting is null || _aiUploadId is null || _aboutOperationInProgress || _operationInProgress) { return; }
        TroubleshootingPageView page = _aboutPage.TroubleshootingPage;
        await RunAboutOperationAsync(async token =>
        {
            using UiOperationLifetime.Lease? lease = _uiOperations.TryEnter(token);
            if (lease is null) { return; }
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(lease.Token);
            _aiCancellation = cancellation;
            _aiReturnRequested = false;
            page.ResetProgress();
            page.SetBusy(true);
            bool receivingProgress = true;
            try
            {
                Progress<string> progress = new(text => { if (receivingProgress && !IsDisposed && !Disposing) { page.StatusLabel.Text = "DeepSeek诊断进行中"; page.AppendProgress(text); } });
                TroubleshootingRunResult result = await _troubleshooting.RunAsync(_aiUploadId, progress, cancellation.Token,
                    PrepareAiReproductionAsync, SetAiRepairControlsAsync);
                receivingProgress = false;
                if (IsDisposed || Disposing) { return; }
                page.StatusLabel.Text = result.ResultPendingUpload ? "排障结束，结果已保存在本机，联网后会补传。" : result.Case.State switch
                {
                    TroubleshootingStates.Completed when result.Case.Plan?.RequestKind != TroubleshootingRequestKinds.Diagnostic => "已回复，本次未进行故障排查。",
                    TroubleshootingStates.Completed => result.Case.Outcome?.Execution == "skipped"
                        ? "诊断已完成，请查看结论和下一步建议。"
                        : "诊断完成，结果已上传。请确认实际修复效果。",
                    TroubleshootingStates.Cancelled => "排障已取消。",
                    _ => TroubleshootingPresentation.Failure(result.Case.ReasonCode),
                };
                if (result.Case.Plan is not null) { page.AppendProgress("分析结论：" + result.Case.Plan.Summary); }
                page.SetHistory([result.Case]);
                await RefreshAiHistoryAsync(token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            { if (!IsDisposed && !Disposing) { page.StatusLabel.Text = "排障已取消；已发生的操作和结果会保留。"; } }
            finally
            {
                receivingProgress = false;
                _aiCancellation = null;
                if (!IsDisposed && !Disposing) { page.SetBusy(false); if (_aiReturnRequested) { ReturnFromAiPage(); } }
            }
        }, page.StatusLabel);
    }

    private void OnAiCancelClicked(object? sender, EventArgs args)
    {
        if (_aiCancellation is not null) { _aiReturnRequested = true; _aiCancellation.Cancel(); }
        else { ReturnFromAiPage(); }
    }

    private void ReturnFromAiPage()
    {
        _aboutPage.HideTroubleshooting();
        _aboutPage.SetDiagnosticReportVisible(true);
        UpdateDiagnosticUploadButton();
    }

    private async void OnAiRefreshClicked(object? sender, EventArgs args) =>
        await RunAboutOperationAsync(RefreshAiHistoryAsync, _aboutPage.TroubleshootingPage.StatusLabel);

    private async void OnAiFeedbackRequested(object? sender, TroubleshootingFeedbackEventArgs args)
    {
        if (_troubleshooting is null) { return; }
        await RunAboutOperationAsync(async token =>
        {
            await _troubleshooting.FeedbackAsync(args.Id, args.Value, token);
            if (!IsDisposed && !Disposing)
            { _aboutPage.TroubleshootingPage.StatusLabel.Text = "反馈已保存，谢谢。"; }
            await RefreshAiHistoryAsync(token);
        }, _aboutPage.TroubleshootingPage.StatusLabel);
    }

    private async Task RefreshAiHistoryAsync(CancellationToken token)
    {
        if (_troubleshooting is null) { return; }
        try
        {
            TroubleshootingCase[] history = await _troubleshooting.HistoryAsync(token);
            if (!IsDisposed && !Disposing) { _aboutPage.TroubleshootingPage.SetHistory(history); }
        }
        catch (Exception)
        {
            if (!IsDisposed && !Disposing && !token.IsCancellationRequested)
            { _aboutPage.TroubleshootingPage.StatusLabel.Text = "暂时无法同步最近记录，诊断包和本机待传结果仍保留。"; }
        }
    }
}
