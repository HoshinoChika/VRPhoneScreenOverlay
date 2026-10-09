namespace VRPhoneScreenOverlay.App;

// User close requests may offer minimize. A successfully handed-off update has
// already been approved and must allow the maintenance process to observe exit.
internal abstract class ExitConfirmationForm : Form
{
    private bool _exitConfirmed;
    private bool _preparingExit;
    private IWindowTray? _tray;
    private bool _changingTrayState;

    protected void ConfigureTray(IWindowTray tray)
    {
        ArgumentNullException.ThrowIfNull(tray);
        if (_tray is not null) { throw new InvalidOperationException("Window tray is already configured."); }
        _tray = tray;
        _tray.RestoreRequested += OnTrayRestoreRequested;
        _tray.ExitRequested += OnTrayExitRequested;
    }

    protected void MinimizeToTray()
    {
        if (_tray is null) { WindowState = FormWindowState.Minimized; return; }
        if (_changingTrayState || IsDisposed || Disposing) { return; }
        _changingTrayState = true;
        try
        {
            _tray.Visible = true;
            Hide();
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
        }
        finally { _changingTrayState = false; }
    }

    private void OnTrayRestoreRequested(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing) { return; }
        _changingTrayState = true;
        try
        {
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Show();
            Activate();
            if (_tray is not null) { _tray.Visible = false; }
        }
        finally { _changingTrayState = false; }
    }

    private void OnTrayExitRequested(object? sender, EventArgs e) => ConfirmExit();

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeTray();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { DisposeTray(); }
        base.Dispose(disposing);
    }

    private void DisposeTray()
    {
        if (_tray is null) { return; }
        _tray.RestoreRequested -= OnTrayRestoreRequested;
        _tray.ExitRequested -= OnTrayExitRequested;
        _tray.Dispose();
        _tray = null;
    }

    protected abstract void ShowExitConfirmation();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exitConfirmed)
        {
            e.Cancel = true;
            if (e.CloseReason == CloseReason.UserClosing) { ShowExitConfirmation(); }
            else { BeginInvoke(ConfirmExit); } // Keep the message loop alive while operations finish.
        }
        base.OnFormClosing(e);
    }

    protected virtual Task PrepareExitAsync() => Task.CompletedTask;
    protected virtual void OnExitPreparationFailed() { }
    protected async void ConfirmExit()
    {
        if (_preparingExit || IsDisposed || Disposing) { return; }
        _preparingExit = true;
        try
        {
            await PrepareExitAsync().ConfigureAwait(true);
            if (IsDisposed || Disposing) { return; }
            _exitConfirmed = true;
            Close();
        }
        catch (Exception) { if (!IsDisposed && !Disposing) { OnExitPreparationFailed(); } }
        finally { _preparingExit = false; }
    }

    protected void CloseAfterMaintenanceStarted(Action launchMaintenance)
    {
        ArgumentNullException.ThrowIfNull(launchMaintenance);
        // Failed handoff must leave the ordinary close confirmation in effect.
        launchMaintenance();
        _exitConfirmed = true;
        BeginInvoke(Close);
    }
}
