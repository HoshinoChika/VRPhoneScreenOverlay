using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private string? _startupOperationMessage;
    private void OnSteamVrStartupStateChanged(object? sender, SteamVrStartupChangedEventArgs args)
    {
        if (_notifications.Dispatch(nameof(OnSteamVrStartupStateChanged), () => OnSteamVrStartupStateChanged(sender, args))) { return; }
        if (IsDisposed || Disposing || _startupService is null) { return; }
        SteamVrStartupStatus current = _startupService.Snapshot;
        var previous = _startupSteamVrWarning;
        bool registrationOwnsWarning = previous is null || previous == _lastStartupRegistration;
        _startupSteamVrWarning = SteamVrStartupPresentation.ResolveWarning(previous, _lastStartupRegistration, current);
        _lastStartupRegistration = registrationOwnsWarning && _startupSteamVrWarning is not null
            ? _startupSteamVrWarning : current.Registration;
        if (current.Registration.Succeeded && !current.Pending)
        {
            if (_startupOperationMessage is not null && _operationStatus.Text == _startupOperationMessage)
            {
                _operationStatus.Text = string.Empty;
                _operationStatus.ForeColor = UiPalette.TextSecondary;
            }
            _startupOperationMessage = null;
        }
        if (_startupOperationMessage is not null && _operationStatus.Text == _startupOperationMessage && current.Attempts > 0)
        {
            var presentation = SteamVrStartupPresentation.Read(current);
            _startupOperationMessage = presentation.Text;
            _operationStatus.Text = presentation.Text;
            _operationStatus.ForeColor = presentation.Warning ? UiPalette.Warning : UiPalette.TextSecondary;
        }
        _settingsView.ApplyStartupStatus(SteamVrStartupPresentation.Read(current));
        UpdateSteamVrBindingNotice(_phoneOverlay.Snapshot);
        UpdateOverlayDashboard(_phoneOverlay.Snapshot);
        UpdatePhoneOverlayButton();
    }

    private void OnBindingSynchronizationChanged(object? sender, BindingSynchronizationChangedEventArgs args)
    {
        if (_notifications.Dispatch(nameof(OnBindingSynchronizationChanged), () => OnBindingSynchronizationChanged(sender, args))) { return; }
        ShowBindingSynchronizationStatus();
        if (args.Snapshot.State == BindingSynchronizationState.Applied)
        { OnBindingGuideRequested(sender, EventArgs.Empty); }
    }

    private void OnSteamVrStartupWarningShown(object? sender, EventArgs args)
    {
        if (_startupSteamVrWarning is not { } warning)
        {
            if (_startupService?.Snapshot is { Attempts: 0, Pending: true })
            {
                _startupOperationMessage = SteamVrStartupPresentation.Registering;
                _operationStatus.Text = _startupOperationMessage;
                _operationStatus.ForeColor = UiPalette.TextSecondary;
            }
            return;
        }
        _startupOperationMessage = UserFacingErrorMessages.FromReason(warning.ReasonCode, warning.Message);
        _operationStatus.Text = _startupOperationMessage;
        _operationStatus.ForeColor = UiPalette.Danger;
    }

    private void ShowBindingSynchronizationStatus()
    {
        if (_bindingSynchronization is null || _bindingGuideView.IsRestoringDefaults) { return; }
        BindingSynchronizationSnapshot snapshot = _bindingSynchronization.Snapshot;
        if (snapshot.State == BindingSynchronizationState.Waiting) { return; }
        _bindingGuideView.OperationStatus.Text = snapshot.State switch
        {
            BindingSynchronizationState.Saved or BindingSynchronizationState.Applying => "正在应用绑定…",
            BindingSynchronizationState.Failed => UserFacingErrorMessages.FromReason(snapshot.ReasonCode, snapshot.Message),
            _ => snapshot.Message,
        };
        _bindingGuideView.OperationStatus.ForeColor = snapshot.State switch
        {
            BindingSynchronizationState.Failed => UiPalette.Danger,
            BindingSynchronizationState.Applied => UiPalette.Success,
            _ => UiPalette.TextSecondary,
        };
    }
}
