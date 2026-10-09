using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private PlayspaceMotionView _motionView = null!;
    private ModernToggle _inertiaToggle = null!;
    private readonly MotionSettingsWriter _motionWriter = null!;
    private MotionPreferences _motionRuntime = MotionPreferences.From(AppSettings.Default);
    private bool _applyingMotion;
    private AppSettings _lastPersistedSettings = AppSettings.Default;

    private static OpenVrPlayspaceMotionOptions MotionOptions(AppSettings settings) => new(
        settings.PlayspaceFlingStrength, settings.PlayspaceGravity,
        settings.PlayspaceFriction, settings.PlayspaceResetAllOffsets);

    private void SetMotionControls(MotionPreferences value)
    {
        bool previous = _updatingSettingsControls;
        _updatingSettingsControls = true;
        try
        {
            _motionView.Multiplier.SelectValue((decimal)value.Multiplier);
            _motionView.Strength.SelectValue((decimal)value.Strength);
            _motionView.Gravity.SelectValue((decimal)value.Gravity);
            _motionView.Friction.SelectValue((decimal)value.Friction);
            _motionView.ResetAllOffsets = value.ResetAll;
            _inertiaToggle.Checked = value.InertiaEnabled;
            _inertiaToggle.Text = value.InertiaEnabled ? "开启" : "关闭";
        }
        finally { _updatingSettingsControls = previous; }
    }

    private void RefreshMotionSettings()
    {
        if (_motionWriter.HasPending) { return; }
        SetMotionControls(_motionRuntime);
    }

    private void ApplyMotionRuntime(MotionPreferences value)
    {
        _applyingMotion = true;
        try
        {
            _playspaceDrag.SetMultiplier(value.Multiplier);
            _playspaceDrag.ConfigureMotion(value.InertiaEnabled, new(value.Strength, value.Gravity, value.Friction, value.ResetAll));
            _motionRuntime = value;
        }
        finally { _applyingMotion = false; }
    }

    private void OnMotionParametersChanged(object? sender, EventArgs args)
    {
        if (_updatingSettingsControls || _uiOperations.IsClosing) { return; }
        MotionPreferences value = new((float)_motionView.Multiplier.Value, _inertiaToggle.Checked,
            (float)_motionView.Strength.Value, (float)_motionView.Gravity.Value, (float)_motionView.Friction.Value,
            _motionView.ResetAllOffsets);
        ApplyMotionRuntime(value);
        _inertiaToggle.Text = value.InertiaEnabled ? "开启" : "关闭";
        _motionView.Status.Text = string.Empty;
        _motionWriter.Request(value);
    }

    private void SyncMotionRuntime(OpenVrPlayspaceDragSnapshot snapshot)
    {
        if (_uiOperations.IsClosing || _applyingMotion || snapshot.MotionOptions is not { } options) { return; }
        MotionPreferences value = new(snapshot.Multiplier, snapshot.FlingEnabled, options.FlingStrength,
            options.Gravity, options.Friction, options.ResetAllOffsets);
        if (value == _motionRuntime) { return; }
        _motionRuntime = value;
        SetMotionControls(value);
        _motionView.Status.Text = string.Empty;
        _motionWriter.Request(value);
    }

    private void OnMotionSaved(bool succeeded)
    {
        if (IsDisposed || Disposing) { return; }
        if (InvokeRequired) { BeginInvoke(() => OnMotionSaved(succeeded)); return; }
        if (succeeded && _motionRuntime != MotionPreferences.From(_settings.Snapshot.Value))
        {
            _motionView.Status.Text = string.Empty;
            return;
        }
        if (succeeded && !_closeConfirmation.IsUpdate) { _closeConfirmation.Description.Text = "最小化保留浮窗，退出关闭浮窗。"; }
        _playspaceDrag.ReportMotionSaveResult(succeeded);
        _motionView.Status.Text = succeeded ? string.Empty : "保存失败，请重新调整或重试退出";
        _motionView.Status.ForeColor = succeeded ? UiPalette.TextSecondary : UiPalette.Warning;
    }

    protected override async Task PrepareExitAsync()
    {
        _closeConfirmation.ExitButton.Enabled = false;
        _closeConfirmation.MinimizeButton.Enabled = false;
        _closeConfirmation.CancelButton.Enabled = false;
        try
        {
            _motionView.Multiplier.Commit();
            _motionView.Strength.Commit();
            _motionView.Gravity.Commit();
            _motionView.Friction.Commit();
            _closeConfirmation.Description.Text = "正在结束操作并保存设置…";
            if (_wirelessView.DeviceManagerHost.Visible) { _wirelessView.DeviceManager.CancelEdit(); }
            await _uiOperations.StopAsync().ConfigureAwait(true);
            await _motionWriter.FlushAsync().ConfigureAwait(true);
        }
        catch
        {
            ResumeAfterExitFailure();
            throw;
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _closeConfirmation.ExitButton.Enabled = true;
                _closeConfirmation.MinimizeButton.Enabled = true;
                _closeConfirmation.CancelButton.Enabled = true;
            }
        }
    }

    private void ResumeAfterExitFailure()
    {
        _uiOperations.Reopen();
        OnControllerDiscoveryShown(this, EventArgs.Empty);
        if (!IsDisposed && !Disposing)
        {
            SetOperationControls(enabled: true);
            SetImmediateSettingControls(enabled: true);
        }
    }

    protected override void OnExitPreparationFailed()
    {
        ShowExitConfirmation();
        _closeConfirmation.Description.Text = "设置未能保存，请稍后重试退出。";
    }

    internal async Task CompleteMotionSavesAsync()
    {
        await _motionWriter.FlushAsync().ConfigureAwait(false);
    }
}
