namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private void OnVideoRecoveryChanged(object? sender, EventArgs args)
    {
        if (_notifications.Dispatch(nameof(OnVideoRecoveryChanged), () => OnVideoRecoveryChanged(sender, args))) { return; }
        // Recovery evidence still gates automatic starts. An explicit open
        // handles any required acknowledgement without adding settings actions.
        ConfigurePhoneAutomation();
        UpdateQualityStatus();
    }
}
