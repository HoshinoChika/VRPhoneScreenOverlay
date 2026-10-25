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
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<(string Text, bool Warning)>? StartupStatusProvider { get; set; }
    private (string Text, bool Warning)? _lastStartupStatus;
    private void OnPicoStatusTick(object? sender, EventArgs e)
    {
        if (PicoStatusProvider is not null) { picoStatus.Text = PicoStatusProvider(); }
        if (StartupStatusProvider is not null) { ApplyStartupStatus(StartupStatusProvider()); }
    }

    internal void ApplyStartupStatus((string Text, bool Warning) next)
    {
        if (next == _lastStartupStatus && (!next.Warning || status.Text == next.Text)) { return; }
        bool ownsCurrentMessage = _lastStartupStatus is { } previous && previous.Text.Length > 0 && status.Text == previous.Text;
        if (!ownsCurrentMessage && status.ForeColor == UiPalette.Danger && status.Text.Length > 0) { return; }
        _lastStartupStatus = next;
        // A successful background check clears only its own previous notice.
        // Keep results and failures from other settings operations visible.
        if (!next.Warning && !ownsCurrentMessage) { return; }
        status.Text = next.Text;
        status.ForeColor = next.Warning ? UiPalette.Warning : UiPalette.TextSecondary;
    }
}
