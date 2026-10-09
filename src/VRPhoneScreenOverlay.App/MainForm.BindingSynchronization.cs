using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private void OnBindingSynchronizationChanged(object? sender, BindingSynchronizationChangedEventArgs args)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) { return; }
        if (InvokeRequired)
        {
            try { BeginInvoke(() => OnBindingSynchronizationChanged(sender, args)); }
            catch (InvalidOperationException) when (IsDisposed || Disposing) { }
            return;
        }
        ShowBindingSynchronizationStatus();
        if (args.Snapshot.State == BindingSynchronizationState.Applied)
        { OnBindingGuideRequested(sender, EventArgs.Empty); }
    }

    private void OnSteamVrStartupWarningShown(object? sender, EventArgs args)
    {
        if (_startupSteamVrWarning is not { } warning) { return; }
        _operationStatus.Text = warning.Message;
        _operationStatus.ForeColor = UiPalette.Danger;
    }

    private void ShowBindingSynchronizationStatus()
    {
        if (_bindingSynchronization is null || _bindingGuideView.IsRestoringDefaults) { return; }
        BindingSynchronizationSnapshot snapshot = _bindingSynchronization.Snapshot;
        if (snapshot.State == BindingSynchronizationState.Waiting) { return; }
        _bindingGuideView.OperationStatus.Text = snapshot.Message;
        _bindingGuideView.OperationStatus.ForeColor = snapshot.State switch
        {
            BindingSynchronizationState.Failed => UiPalette.Danger,
            BindingSynchronizationState.Applied => UiPalette.Success,
            _ => UiPalette.TextSecondary,
        };
    }
}
