using VRPhoneScreenOverlay.Diagnostics;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private DiagnosticCaptureService? _captureService;
    private System.Windows.Forms.Timer? _capturePulseTimer;
    private string? _lastCaptureStatus;

    private void InitializeCapture(DiagnosticCaptureService? service)
    {
        _captureService = service;
        _aboutPage.CaptureButton.Enabled = service is not null;
        _aboutPage.ExportCaptureButton.Enabled = service is not null;
        _aboutPage.CaptureButton.Click += OnCaptureClicked;
        _aboutPage.ExportCaptureButton.Click += OnExportCaptureClicked;
        if (service is null) { return; }
        _capturePulseTimer = new() { Interval = 1000 };
        _capturePulseTimer.Tick += (_, _) => { service.Pulse(); RefreshCapture(); };
        _capturePulseTimer.Start();
    }

    private async void OnExportCaptureClicked(object? sender, EventArgs args)
    {
        if (_captureService is null || _aboutOperationInProgress) { return; }
        _aboutPage.ExportCaptureButton.Enabled = false;
        try
        {
            await RunAboutOperationAsync(async token =>
            {
                if (_captureService.IsObserving) { await _captureService.StopAsync(token); }
                string? exported = await _captureService.ExportLatestAsync(token);
                RefreshCapture();
                _aboutPage.CaptureStatus.Text = exported is null ? "暂无故障转储；软件会在发生故障时自动保存。"
                    : "已导出到本机 captures/exports；含内存片段，未上传。";
            }, _aboutPage.CaptureStatus);
        }
        finally
        {
            if (!IsDisposed && !Disposing) { _aboutPage.ExportCaptureButton.Enabled = true; }
        }
    }

    private async void OnCaptureClicked(object? sender, EventArgs args)
    {
        if (_captureService is null || _aboutOperationInProgress) { return; }
        _aboutPage.CaptureButton.Enabled = false;
        try
        {
            await RunAboutOperationAsync(async token =>
            {
                if (_captureService.IsObserving) { await _captureService.StopAsync(token); }
                else { _ = await _captureService.StartAutomaticAsync(token); }
                RefreshCapture();
            }, _aboutPage.CaptureStatus);
        }
        finally
        {
            if (!IsDisposed && !Disposing) { _aboutPage.CaptureButton.Enabled = true; }
        }
    }

    private void RefreshCapture()
    {
        if (_captureService is null || _lastCaptureStatus == _captureService.Status) { return; }
        _lastCaptureStatus = _captureService.Status;
        _aboutPage.SetCaptureStatus(_lastCaptureStatus);
    }
}
