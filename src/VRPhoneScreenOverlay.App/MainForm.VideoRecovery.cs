namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private void OnVideoRecoveryChanged(object? sender, EventArgs args)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) { return; }
        if (InvokeRequired)
        {
            try { BeginInvoke(() => OnVideoRecoveryChanged(sender, args)); }
            catch (InvalidOperationException) { }
            return;
        }
        // Recovery evidence still gates automatic starts. An explicit open
        // handles any required acknowledgement without adding settings actions.
        ConfigurePhoneAutomation();
        UpdateQualityStatus();
    }
}
