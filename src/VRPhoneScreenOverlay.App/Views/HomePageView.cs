namespace VRPhoneScreenOverlay.App.Views;

public partial class HomePageView : UserControl
{
    public HomePageView()
    {
        InitializeComponent();
        steamVrIcon.SetBundledApplicationIcon(BundledApplicationIcon.SteamVr);
        wirelessButton.Click += (_, _) => ShowWireless();
        wirelessPanel.BackButton.Click += (_, _) => HideWireless();
        motionButton.Click += (_, _) => ShowMotion();
        motionPanel.BackButton.Click += (_, _) => HideMotion();
        DeviceList = wirelessPanel.DeviceList;
        RefreshButton = wirelessPanel.RefreshButton;
        VideoProbeButton = new Button { Visible = false };
        OperationStatus = wirelessPanel.Status;
        Controls.Add(VideoProbeButton);
    }

    internal PlayspaceMotionView MotionPanel => motionPanel;

    internal void ShowMotion()
    {
        deviceCard.Visible = false;
        metricsCard.Visible = false;
        playspaceCard.Visible = false;
        motionPanel.Visible = true;
        motionPanel.BringToFront();
    }

    private void HideMotion()
        => ShowMainContent();

    internal WirelessConnectionView WirelessPanel => wirelessPanel;

    internal void ShowWireless()
    {
        deviceCard.Visible = false;
        metricsCard.Visible = false;
        playspaceCard.Visible = false;
        wirelessPanel.Visible = true;
        wirelessPanel.BringToFront();
    }

    private void HideWireless()
        => ShowMainContent();

    internal void ShowMainContent()
    {
        // Hide first so closing the device editor cannot restart QR pairing.
        wirelessPanel.Visible = false;
        motionPanel.Visible = false;
        wirelessPanel.DeviceManager.CancelEdit();
        wirelessPanel.HideDeviceManager();
        wirelessPanel.ClearPairingCode();
        wirelessPanel.ShowQr(null);
        deviceCard.Visible = true;
        metricsCard.Visible = true;
        playspaceCard.Visible = true;
    }

    internal Label PhoneStatus => phoneStatus;
    internal Label PhoneName => phoneTitle;
    internal Label PhoneDetails => phoneDetails;
    internal Label AdbDetail => adbDetail;
    internal Label VideoStatus => videoStatus;
    internal Label AudioStatus => audioStatus;
    internal Label ControlStatus => controlStatus;
    internal Label SteamVrStatus => steamVrStatus;
    internal ModernButton OpenOverlayButton => openOverlayButton;
    internal Label MetricResolution => metricResolution;
    internal Label MetricBitrate => metricBitrate;
    internal Label MetricFrameRate => metricFrameRate;
    internal Label MetricLatency => metricLatency;
    internal Label PlayspaceStatus => playspaceStatus;
    internal Label PlayspaceCoordinates => playspaceCoordinates;
    internal ModernToggle InertiaToggle => inertiaToggle;
    internal ModernToggle PlayspaceToggle => playspaceToggle;
    internal ListBox DeviceList { get; }
    internal Button RefreshButton { get; }
    internal Button VideoProbeButton { get; }
    internal Label OperationStatus { get; }
}
