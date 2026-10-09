using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private readonly StartupUpdateGate _startupUpdateGate = new();
    private bool _pendingUpdatePrompt;
    private bool _installingUpdate;
    private CancellationTokenSource? _updateDownloadCancellation;
    private DateTimeOffset? _lastUpdateCheckAt;
    private string? _lastUpdateFailureKind;
    private int? _lastUpdateHttpStatus;
    private string _lastUpdateReason = UpdateReasonCodes.NotChecked;

    private async void OnStartupUpdateShown(object? sender, EventArgs args)
    {
        await AboutOperationRunner.RunAsync(async token =>
        {
            if (await _startupUpdateGate.WaitOnceAsync(token).ConfigureAwait(true) && !IsDisposed && !_aboutOperationInProgress)
            { await RunAboutOperationAsync(deadline => CheckForUpdatesAsync(true, deadline)).ConfigureAwait(true); }
        }, _formLifetime.Token).ConfigureAwait(true);
    }

    private async Task CheckForUpdatesAsync(bool startup, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(startup ? 20 : 45));
        _lastUpdateCheckAt = DateTimeOffset.UtcNow;
        _lastUpdateReason = UpdateReasonCodes.Checking;
        _lastUpdateFailureKind = null;
        _lastUpdateHttpStatus = null;
        SetControlText(_aboutOperationStatus, "正在检查更新…");
        try
        {
            UpdateCheckResult result = await _updateService.CheckAsync(DisplayVersion, deadline.Token).ConfigureAwait(true);
            _lastUpdateReason = result.ReasonCode;
            _availableUpdate = result.UpdateAvailable ? result.Release : null;
            SetControlText(_aboutOperationStatus, result.Message);
            SetControlForeColor(_aboutOperationStatus, result.UpdateAvailable ? UiPalette.Success : UiPalette.TextSecondary);
            SetControlText(_checkUpdateButton, "检查更新");
            _pendingUpdatePrompt = _availableUpdate is not null;
            PresentPendingUpdate();
        }
        catch (Exception exception)
        {
            _lastUpdateReason = deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested
                ? NetworkReasonCodes.Timeout : exception is IReasonCoded coded ? coded.ReasonCode : UpdateReasonCodes.CheckFailed;
            _lastUpdateHttpStatus = (exception as NetworkException)?.StatusCode;
            _lastUpdateFailureKind = exception.InnerException is HttpRequestException http
                ? http.HttpRequestError.ToString() : exception.GetType().Name;
            if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            { throw new NetworkException(NetworkReasonCodes.Timeout, "检查更新超时，请稍后手动重试", inner: exception); }
            throw;
        }
    }

    private void PresentPendingUpdate()
    {
        if (!_pendingUpdatePrompt || _availableUpdate is null || !Visible || IsDisposed || Disposing || _closeConfirmation.Visible) { return; }
        _pendingUpdatePrompt = false;
        _closeConfirmation.ConfigureUpdate(_availableUpdate.Version, _availableUpdate.ReleaseNotes);
        _closeConfirmation.Visible = true;
        _closeConfirmation.BringToFront();
        _closeConfirmation.MinimizeButton.Focus();
    }

    private async void OnConfirmationSecondaryClicked(object? sender, EventArgs args)
    {
        await AboutOperationRunner.RunAsync(async _ =>
        {
            if (!_closeConfirmation.IsUpdate) { ConfirmExit(); return; }
            if (_installingUpdate && _updateDownloadCancellation is { } stop)
            {
                _closeConfirmation.Description.Text = "正在取消下载…";
                await stop.CancelAsync().ConfigureAwait(true);
                return;
            }
            _pendingUpdatePrompt = false;
            _closeConfirmation.Visible = false;
        }, _formLifetime.Token).ConfigureAwait(true);
    }

    private async void OnConfirmationPrimaryClicked(object? sender, EventArgs args)
    {
        if (!_closeConfirmation.IsUpdate)
        {
            _closeConfirmation.Visible = false;
            MinimizeToTray();
            return;
        }
        if (_availableUpdate is not { } release || _installingUpdate) { return; }
        _installingUpdate = true;
        _closeConfirmation.MinimizeButton.Enabled = false;
        _closeConfirmation.ExitButton.Enabled = true;
        _closeConfirmation.ExitButton.Text = "取消下载";
        try
        {
            await RunAboutOperationAsync(async token =>
            {
                Progress<UpdateDownloadProgress> progress = new(value =>
                {
                    if (IsDisposed || Disposing) { return; }
                    int percent = value.TotalBytes > 0 ? (int)Math.Clamp(value.ReceivedBytes * 100 / value.TotalBytes, 0, 100) : 0;
                    _closeConfirmation.Description.Text = $"正在下载更新… {percent}%";
                });
                using CancellationTokenSource download = CancellationTokenSource.CreateLinkedTokenSource(token);
                download.CancelAfter(TimeSpan.FromMinutes(10));
                _updateDownloadCancellation = download;
                _lastUpdateReason = UpdateReasonCodes.Downloading;
                _lastUpdateHttpStatus = null;
                try
                {
                    _closeConfirmation.Description.Text = "正在准备更新…";
                    PreparedUpdate prepared = await _updateService.DownloadAndStageAsync(release, progress, download.Token).ConfigureAwait(true);
                    download.Token.ThrowIfCancellationRequested();
                    _lastUpdateReason = UpdateReasonCodes.Restarting;
                    _closeConfirmation.Description.Text = "校验通过，正在重启…";
                    await PrepareExitAsync().ConfigureAwait(true);
                    try
                    {
                        download.Token.ThrowIfCancellationRequested();
                        CloseAfterMaintenanceStarted(() => _updateService.LaunchMaintenance(prepared, Environment.ProcessId));
                    }
                    catch { ResumeAfterExitFailure(); throw; }
                }
                catch (Exception exception)
                {
                    _lastUpdateReason = exception is IReasonCoded coded ? coded.ReasonCode : UpdateReasonCodes.InstallFailed;
                    _lastUpdateHttpStatus = (exception as NetworkException)?.StatusCode;
                    _lastUpdateFailureKind = exception.InnerException is HttpRequestException http
                        ? http.HttpRequestError.ToString() : exception.GetType().Name;
                    throw;
                }
                finally { _updateDownloadCancellation = null; }
            }).ConfigureAwait(true);
        }
        finally
        {
            _installingUpdate = false;
            if (!IsDisposed && !Disposing)
            {
                _closeConfirmation.MinimizeButton.Enabled = true;
                _closeConfirmation.ExitButton.Enabled = true;
                _closeConfirmation.ExitButton.Text = "暂不更新";
            }
        }
    }
}
