using System.ComponentModel;

namespace VRPhoneScreenOverlay.App.Views;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView() { InitializeComponent(); }
    internal ModernToggle KeepAwakeWhileGrabbedToggle => keepAwakeToggle;
    internal ModernToggle VrUnlockKeypadToggle => unlockKeypadToggle;
    internal ModernToggle AutoOpenToggle => autoOpenToggle;
    internal ModernToggle ScreenOffToggle => screenOffToggle;
    internal ModernToggle SteamVrToggle => steamVrToggle;
    internal ModernToggle MinimizeToggle => minimizeToggle;
    internal Label Status => status;
    internal ModernToggle PicoToggle => picoToggle;
    internal Label PicoStatus => picoStatus;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string>? PicoStatusProvider { get; set; }
    private void OnPicoStatusTick(object? sender, EventArgs e)
    {
        if (PicoStatusProvider is not null) { picoStatus.Text = PicoStatusProvider(); }
    }
}
