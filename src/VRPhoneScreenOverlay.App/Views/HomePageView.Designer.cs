#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class HomePageView
{
    private System.ComponentModel.IContainer components = null;
    private ModernButton motionButton;
    private ModernToggle inertiaToggle;
    private PlayspaceMotionView motionPanel;
    private ModernButton wirelessButton;
    private WirelessConnectionView wirelessPanel;
    private SurfacePanel deviceCard = null!;
    private Label deviceTitle = null!;
    private SurfacePanel adbChip = null!;
    private ModernIconView adbIcon = null!;
    private Label adbTitle = null!;
    private Label phoneStatus = null!;
    private Label adbDetail = null!;
    private Panel adbDivider = null!;
    private SurfacePanel phoneChip = null!;
    private ModernIconView phoneIcon = null!;
    private Label phoneTitle = null!;
    private Label phoneDetails = null!;
    private Panel phoneDivider = null!;
    private SurfacePanel videoChip = null!;
    private ModernIconView videoIcon = null!;
    private Label videoTitle = null!;
    private Label videoStatus = null!;
    private Panel videoDivider = null!;
    private SurfacePanel audioChip = null!;
    private ModernIconView audioIcon = null!;
    private Label audioTitle = null!;
    private Label audioStatus = null!;
    private Panel audioDivider = null!;
    private SurfacePanel controlChip = null!;
    private ModernIconView controlIcon = null!;
    private Label controlTitle = null!;
    private Label controlStatus = null!;
    private Panel controlDivider = null!;
    private SurfacePanel steamVrChip = null!;
    private ModernIconView steamVrIcon = null!;
    private Label steamVrTitle = null!;
    private Label steamVrStatus = null!;
    private Panel steamVrDivider = null!;
    private ModernButton openOverlayButton = null!;
    private SurfacePanel metricsCard = null!;
    private Label metricsTitle = null!;
    private SurfacePanel resolutionCard = null!;
    private Label resolutionTitle = null!;
    private Label metricResolution = null!;
    private SurfacePanel bitrateCard = null!;
    private Label bitrateTitle = null!;
    private Label metricBitrate = null!;
    private SurfacePanel frameRateCard = null!;
    private Label frameRateTitle = null!;
    private Label metricFrameRate = null!;
    private SurfacePanel latencyCard = null!;
    private Label latencyTitle = null!;
    private Label metricLatency = null!;
    private SurfacePanel playspaceCard = null!;
    private Label playspaceTitle = null!;
    private Label playspaceStatusTitle = null!;
    private Label playspaceStatus = null!;
    private ModernToggle playspaceToggle = null!;
    private Panel playspaceDivider = null!;
    private Label coordinateTitle = null!;
    private Label playspaceCoordinates = null!;
    private Label inertiaTitle = null!;

    private ContextHelpIcon playspaceStatusTitleHelp;
    private ContextHelpIcon inertiaTitleHelp;
    private ContextHelpIcon coordinateTitleHelp;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        playspaceStatusTitleHelp = new ContextHelpIcon();
        inertiaTitleHelp = new ContextHelpIcon();
        coordinateTitleHelp = new ContextHelpIcon();

        motionButton = new ModernButton();
        inertiaToggle = new ModernToggle();
        motionPanel = new PlayspaceMotionView();
        wirelessButton = new ModernButton();
        wirelessPanel = new WirelessConnectionView();
        deviceCard = new SurfacePanel();
        deviceTitle = new Label();
        adbChip = new SurfacePanel();
        adbIcon = new ModernIconView();
        adbTitle = new Label();
        phoneStatus = new Label();
        adbDetail = new Label();
        adbDivider = new Panel();
        phoneChip = new SurfacePanel();
        phoneIcon = new ModernIconView();
        phoneTitle = new Label();
        phoneDetails = new Label();
        phoneDivider = new Panel();
        videoChip = new SurfacePanel();
        videoIcon = new ModernIconView();
        videoTitle = new Label();
        videoStatus = new Label();
        videoDivider = new Panel();
        audioChip = new SurfacePanel();
        audioIcon = new ModernIconView();
        audioTitle = new Label();
        audioStatus = new Label();
        audioDivider = new Panel();
        controlChip = new SurfacePanel();
        controlIcon = new ModernIconView();
        controlTitle = new Label();
        controlStatus = new Label();
        controlDivider = new Panel();
        steamVrChip = new SurfacePanel();
        steamVrIcon = new ModernIconView();
        steamVrTitle = new Label();
        steamVrStatus = new Label();
        steamVrDivider = new Panel();
        openOverlayButton = new ModernButton();
        metricsCard = new SurfacePanel();
        metricsTitle = new Label();
        resolutionCard = new SurfacePanel();
        resolutionTitle = new Label();
        metricResolution = new Label();
        bitrateCard = new SurfacePanel();
        bitrateTitle = new Label();
        metricBitrate = new Label();
        frameRateCard = new SurfacePanel();
        frameRateTitle = new Label();
        metricFrameRate = new Label();
        latencyCard = new SurfacePanel();
        latencyTitle = new Label();
        metricLatency = new Label();
        playspaceCard = new SurfacePanel();
        playspaceTitle = new Label();
        playspaceStatusTitle = new Label();
        playspaceStatus = new Label();
        playspaceToggle = new ModernToggle();
        playspaceDivider = new Panel();
        coordinateTitle = new Label();
        playspaceCoordinates = new Label();
        inertiaTitle = new Label();
        deviceCard.SuspendLayout();
        adbChip.SuspendLayout();
        phoneChip.SuspendLayout();
        videoChip.SuspendLayout();
        audioChip.SuspendLayout();
        controlChip.SuspendLayout();
        steamVrChip.SuspendLayout();
        metricsCard.SuspendLayout();
        resolutionCard.SuspendLayout();
        bitrateCard.SuspendLayout();
        frameRateCard.SuspendLayout();
        latencyCard.SuspendLayout();
        playspaceCard.SuspendLayout();
        SuspendLayout();
        // playspaceStatusTitleHelp
        playspaceStatusTitleHelp.Name = "playspaceStatusTitleHelp";
        playspaceStatusTitleHelp.Location = new Point(76, 45);
        playspaceStatusTitleHelp.Size = new Size(22, 22);
        playspaceStatusTitleHelp.HelpText = UiHelpContent.Drag;
        playspaceStatusTitleHelp.TargetLabel = playspaceStatusTitle;
        playspaceStatusTitleHelp.AccessibleName = "空间拖拽说明";
        // inertiaTitleHelp
        inertiaTitleHelp.Name = "inertiaTitleHelp";
        inertiaTitleHelp.Location = new Point(128, 85);
        inertiaTitleHelp.Size = new Size(22, 22);
        inertiaTitleHelp.HelpText = UiHelpContent.Inertia;
        inertiaTitleHelp.TargetLabel = inertiaTitle;
        inertiaTitleHelp.AccessibleName = "惯性与重力说明";
        // coordinateTitleHelp
        coordinateTitleHelp.Name = "coordinateTitleHelp";
        coordinateTitleHelp.Location = new Point(82, 120);
        coordinateTitleHelp.Size = new Size(22, 22);
        coordinateTitleHelp.HelpText = UiHelpContent.Coordinates;
        coordinateTitleHelp.TargetLabel = coordinateTitle;
        coordinateTitleHelp.AccessibleName = "空间坐标说明";

        // deviceCard
        deviceCard.BorderColor = UiPalette.BorderSoft;
        deviceCard.Controls.Add(deviceTitle);
        deviceCard.Controls.Add(adbChip);
        deviceCard.Controls.Add(adbTitle);
        deviceCard.Controls.Add(phoneStatus);
        deviceCard.Controls.Add(adbDetail);
        deviceCard.Controls.Add(adbDivider);
        deviceCard.Controls.Add(phoneChip);
        deviceCard.Controls.Add(phoneTitle);
        deviceCard.Controls.Add(phoneDetails);
        deviceCard.Controls.Add(phoneDivider);
        deviceCard.Controls.Add(videoChip);
        deviceCard.Controls.Add(videoTitle);
        deviceCard.Controls.Add(videoStatus);
        deviceCard.Controls.Add(videoDivider);
        deviceCard.Controls.Add(audioChip);
        deviceCard.Controls.Add(audioTitle);
        deviceCard.Controls.Add(audioStatus);
        deviceCard.Controls.Add(audioDivider);
        deviceCard.Controls.Add(controlChip);
        deviceCard.Controls.Add(controlTitle);
        deviceCard.Controls.Add(controlStatus);
        deviceCard.Controls.Add(controlDivider);
        deviceCard.Controls.Add(steamVrChip);
        deviceCard.Controls.Add(steamVrTitle);
        deviceCard.Controls.Add(steamVrStatus);
        deviceCard.Controls.Add(steamVrDivider);
        deviceCard.Controls.Add(openOverlayButton);
        deviceCard.CornerRadius = 14;
        deviceCard.Location = new Point(0, 0);
        deviceCard.Name = "deviceCard";
        deviceCard.Size = new Size(354, 424);
        deviceCard.SurfaceColor = UiPalette.Surface;
        deviceCard.Controls.Add(wirelessButton);
        wirelessButton.Location = new Point(232, 8);
        wirelessButton.Size = new Size(106, 28);
        wirelessButton.Text = "连接管理";
        wirelessButton.Name = "wirelessButton";
        wirelessPanel.Location = Point.Empty;
        wirelessPanel.Visible = false;
        wirelessPanel.Name = "wirelessPanel";
        Controls.Add(wirelessPanel);
        // deviceTitle
        deviceTitle.AutoSize = false;
        deviceTitle.BackColor = Color.Transparent;
        deviceTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        deviceTitle.ForeColor = UiPalette.TextPrimary;
        deviceTitle.Location = new Point(16, 10);
        deviceTitle.Name = "deviceTitle";
        deviceTitle.Size = new Size(200, 26);
        deviceTitle.Text = "设备与链路";
        deviceTitle.UseMnemonic = false;
        // adbChip
        adbChip.BorderColor = Color.Transparent;
        adbChip.Controls.Add(adbIcon);
        adbChip.CornerRadius = 7;
        adbChip.Location = new Point(16, 44);
        adbChip.Name = "adbChip";
        adbChip.Size = new Size(28, 28);
        adbChip.SurfaceColor = Color.Transparent;
        // adbIcon
        adbIcon.BackColor = Color.Transparent;
        adbIcon.Icon = UiIcons.Usb;
        adbIcon.IconColor = UiPalette.Accent;
        adbIcon.Location = new Point(0, 0);
        adbIcon.Name = "adbIcon";
        adbIcon.Size = new Size(28, 28);
        // adbTitle
        adbTitle.AutoSize = false;
        adbTitle.BackColor = Color.Transparent;
        adbTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        adbTitle.ForeColor = UiPalette.TextPrimary;
        adbTitle.Location = new Point(54, 41);
        adbTitle.Name = "adbTitle";
        adbTitle.Size = new Size(150, 22);
        adbTitle.Text = "ADB";
        adbTitle.UseMnemonic = false;
        // phoneStatus
        phoneStatus.AutoSize = false;
        phoneStatus.BackColor = Color.Transparent;
        phoneStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        phoneStatus.ForeColor = UiPalette.TextPrimary;
        phoneStatus.Location = new Point(228, 41);
        phoneStatus.Name = "phoneStatus";
        phoneStatus.Size = new Size(110, 22);
        phoneStatus.Text = "等待设备";
        phoneStatus.TextAlign = ContentAlignment.MiddleRight;
        phoneStatus.UseMnemonic = false;
        // adbDetail
        adbDetail.AutoSize = false;
        adbDetail.BackColor = Color.Transparent;
        adbDetail.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        adbDetail.ForeColor = UiPalette.TextPrimary;
        adbDetail.Location = new Point(54, 64);
        adbDetail.Name = "adbDetail";
        adbDetail.Size = new Size(284, 34);
        adbDetail.Text = "等待 USB 设备";
        adbDetail.UseMnemonic = false;
        // adbDivider
        adbDivider.BackColor = UiPalette.BorderSoft;
        adbDivider.Location = new Point(54, 102);
        adbDivider.Name = "adbDivider";
        adbDivider.Size = new Size(284, 1);
        // phoneChip
        phoneChip.BorderColor = Color.Transparent;
        phoneChip.Controls.Add(phoneIcon);
        phoneChip.CornerRadius = 7;
        phoneChip.Location = new Point(16, 112);
        phoneChip.Name = "phoneChip";
        phoneChip.Size = new Size(28, 28);
        phoneChip.SurfaceColor = Color.Transparent;
        // phoneIcon
        phoneIcon.BackColor = Color.Transparent;
        phoneIcon.Icon = UiIcons.Phone;
        phoneIcon.IconColor = UiPalette.Accent;
        phoneIcon.Location = new Point(0, 0);
        phoneIcon.Name = "phoneIcon";
        phoneIcon.Size = new Size(28, 28);
        // phoneTitle
        phoneTitle.AutoSize = false;
        phoneTitle.BackColor = Color.Transparent;
        phoneTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        phoneTitle.ForeColor = UiPalette.TextPrimary;
        phoneTitle.Location = new Point(54, 109);
        phoneTitle.Name = "phoneTitle";
        phoneTitle.Size = new Size(284, 22);
        phoneTitle.Text = "手机";
        phoneTitle.UseMnemonic = false;
        // phoneDetails
        phoneDetails.AutoSize = false;
        phoneDetails.BackColor = Color.Transparent;
        phoneDetails.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        phoneDetails.ForeColor = UiPalette.TextPrimary;
        phoneDetails.Location = new Point(54, 132);
        phoneDetails.Name = "phoneDetails";
        phoneDetails.Size = new Size(284, 38);
        phoneDetails.Text = "未选择手机";
        phoneDetails.UseMnemonic = false;
        // phoneDivider
        phoneDivider.BackColor = UiPalette.BorderSoft;
        phoneDivider.Location = new Point(54, 174);
        phoneDivider.Name = "phoneDivider";
        phoneDivider.Size = new Size(284, 1);
        // videoChip
        videoChip.BorderColor = Color.Transparent;
        videoChip.Controls.Add(videoIcon);
        videoChip.CornerRadius = 7;
        videoChip.Location = new Point(16, 184);
        videoChip.Name = "videoChip";
        videoChip.Size = new Size(28, 28);
        videoChip.SurfaceColor = Color.Transparent;
        // videoIcon
        videoIcon.BackColor = Color.Transparent;
        videoIcon.Icon = UiIcons.Video;
        videoIcon.IconColor = UiPalette.Accent;
        videoIcon.Location = new Point(0, 0);
        videoIcon.Name = "videoIcon";
        videoIcon.Size = new Size(28, 28);
        // videoTitle
        videoTitle.AutoSize = false;
        videoTitle.BackColor = Color.Transparent;
        videoTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        videoTitle.ForeColor = UiPalette.TextPrimary;
        videoTitle.Location = new Point(54, 181);
        videoTitle.Name = "videoTitle";
        videoTitle.Size = new Size(150, 22);
        videoTitle.Text = "视频流";
        videoTitle.UseMnemonic = false;
        // videoStatus
        videoStatus.AutoSize = false;
        videoStatus.BackColor = Color.Transparent;
        videoStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        videoStatus.ForeColor = UiPalette.TextPrimary;
        videoStatus.Location = new Point(210, 181);
        videoStatus.Name = "videoStatus";
        videoStatus.Size = new Size(116, 22);
        videoStatus.Text = "未运行";
        videoStatus.TextAlign = ContentAlignment.MiddleRight;
        videoStatus.UseMnemonic = false;
        // videoDivider
        videoDivider.BackColor = UiPalette.BorderSoft;
        videoDivider.Location = new Point(54, 224);
        videoDivider.Name = "videoDivider";
        videoDivider.Size = new Size(284, 1);
        // audioChip
        audioChip.BorderColor = Color.Transparent;
        audioChip.Controls.Add(audioIcon);
        audioChip.CornerRadius = 7;
        audioChip.Location = new Point(16, 234);
        audioChip.Name = "audioChip";
        audioChip.Size = new Size(28, 28);
        audioChip.SurfaceColor = Color.Transparent;
        // audioIcon
        audioIcon.BackColor = Color.Transparent;
        audioIcon.Icon = UiIcons.Audio;
        audioIcon.IconColor = UiPalette.Accent;
        audioIcon.Location = new Point(0, 0);
        audioIcon.Name = "audioIcon";
        audioIcon.Size = new Size(28, 28);
        // audioTitle
        audioTitle.AutoSize = false;
        audioTitle.BackColor = Color.Transparent;
        audioTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        audioTitle.ForeColor = UiPalette.TextPrimary;
        audioTitle.Location = new Point(54, 231);
        audioTitle.Name = "audioTitle";
        audioTitle.Size = new Size(150, 22);
        audioTitle.Text = "音频流";
        audioTitle.UseMnemonic = false;
        // audioStatus
        audioStatus.AutoSize = false;
        audioStatus.BackColor = Color.Transparent;
        audioStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        audioStatus.ForeColor = UiPalette.TextPrimary;
        audioStatus.Location = new Point(210, 231);
        audioStatus.Name = "audioStatus";
        audioStatus.Size = new Size(116, 22);
        audioStatus.Text = "未运行";
        audioStatus.TextAlign = ContentAlignment.MiddleRight;
        audioStatus.UseMnemonic = false;
        // audioDivider
        audioDivider.BackColor = UiPalette.BorderSoft;
        audioDivider.Location = new Point(54, 274);
        audioDivider.Name = "audioDivider";
        audioDivider.Size = new Size(284, 1);
        // controlChip
        controlChip.BorderColor = Color.Transparent;
        controlChip.Controls.Add(controlIcon);
        controlChip.CornerRadius = 7;
        controlChip.Location = new Point(16, 284);
        controlChip.Name = "controlChip";
        controlChip.Size = new Size(28, 28);
        controlChip.SurfaceColor = Color.Transparent;
        // controlIcon
        controlIcon.BackColor = Color.Transparent;
        controlIcon.Icon = UiIcons.Control;
        controlIcon.IconColor = UiPalette.Accent;
        controlIcon.Location = new Point(0, 0);
        controlIcon.Name = "controlIcon";
        controlIcon.Size = new Size(28, 28);
        // controlTitle
        controlTitle.AutoSize = false;
        controlTitle.BackColor = Color.Transparent;
        controlTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        controlTitle.ForeColor = UiPalette.TextPrimary;
        controlTitle.Location = new Point(54, 281);
        controlTitle.Name = "controlTitle";
        controlTitle.Size = new Size(150, 22);
        controlTitle.Text = "控制流";
        controlTitle.UseMnemonic = false;
        // controlStatus
        controlStatus.AutoSize = false;
        controlStatus.BackColor = Color.Transparent;
        controlStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        controlStatus.ForeColor = UiPalette.TextPrimary;
        controlStatus.Location = new Point(210, 281);
        controlStatus.Name = "controlStatus";
        controlStatus.Size = new Size(116, 22);
        controlStatus.Text = "未运行";
        controlStatus.TextAlign = ContentAlignment.MiddleRight;
        controlStatus.UseMnemonic = false;
        // controlDivider
        controlDivider.BackColor = UiPalette.BorderSoft;
        controlDivider.Location = new Point(54, 312);
        controlDivider.Name = "controlDivider";
        controlDivider.Size = new Size(184, 1);
        // steamVrChip
        steamVrChip.BorderColor = Color.Transparent;
        steamVrChip.Controls.Add(steamVrIcon);
        steamVrChip.CornerRadius = 7;
        steamVrChip.Location = new Point(16, 330);
        steamVrChip.Name = "steamVrChip";
        steamVrChip.Size = new Size(28, 28);
        steamVrChip.SurfaceColor = Color.Transparent;
        // steamVrIcon
        steamVrIcon.BackColor = Color.Transparent;
        steamVrIcon.Icon = UiIcons.SteamVr;
        steamVrIcon.IconColor = UiPalette.Accent;
        steamVrIcon.Location = new Point(0, 0);
        steamVrIcon.Name = "steamVrIcon";
        steamVrIcon.Size = new Size(28, 28);
        // steamVrTitle
        steamVrTitle.AutoSize = false;
        steamVrTitle.BackColor = Color.Transparent;
        steamVrTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        steamVrTitle.ForeColor = UiPalette.TextPrimary;
        steamVrTitle.Location = new Point(54, 321);
        steamVrTitle.Name = "steamVrTitle";
        steamVrTitle.Size = new Size(170, 22);
        steamVrTitle.Text = "SteamVR";
        steamVrTitle.UseMnemonic = false;
        // steamVrStatus
        steamVrStatus.AutoSize = false;
        steamVrStatus.BackColor = Color.Transparent;
        steamVrStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        steamVrStatus.ForeColor = UiPalette.TextPrimary;
        steamVrStatus.Location = new Point(54, 346);
        steamVrStatus.Name = "steamVrStatus";
        steamVrStatus.Size = new Size(170, 22);
        steamVrStatus.Text = "等待连接";
        steamVrStatus.TextAlign = ContentAlignment.MiddleLeft;
        steamVrStatus.UseMnemonic = false;
        // steamVrDivider
        steamVrDivider.BackColor = UiPalette.BorderSoft;
        steamVrDivider.Location = new Point(54, 404);
        steamVrDivider.Name = "steamVrDivider";
        steamVrDivider.Size = new Size(184, 1);
        // openOverlayButton
        openOverlayButton.Icon = UiIcons.Play;
        openOverlayButton.Location = new Point(254, 313);
        openOverlayButton.Name = "openOverlayButton";
        openOverlayButton.Size = new Size(84, 91);
        openOverlayButton.Text = "开启手机浮窗";
        openOverlayButton.Tone = UiButtonTone.Success;
        openOverlayButton.VerticalContent = true;
        // metricsCard
        metricsCard.BorderColor = UiPalette.BorderSoft;
        metricsCard.Controls.Add(metricsTitle);
        metricsCard.Controls.Add(resolutionCard);
        metricsCard.Controls.Add(bitrateCard);
        metricsCard.Controls.Add(frameRateCard);
        metricsCard.Controls.Add(latencyCard);
        metricsCard.CornerRadius = 14;
        metricsCard.Location = new Point(366, 0);
        metricsCard.Name = "metricsCard";
        metricsCard.Size = new Size(230, 196);
        metricsCard.SurfaceColor = UiPalette.Surface;
        // metricsTitle
        metricsTitle.AutoSize = false;
        metricsTitle.BackColor = Color.Transparent;
        metricsTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        metricsTitle.ForeColor = UiPalette.TextPrimary;
        metricsTitle.Location = new Point(14, 10);
        metricsTitle.Name = "metricsTitle";
        metricsTitle.Size = new Size(160, 24);
        metricsTitle.Text = "运行数据";
        // resolutionCard
        resolutionCard.BorderColor = Color.Transparent;
        resolutionCard.Controls.Add(resolutionTitle);
        resolutionCard.Controls.Add(metricResolution);
        resolutionCard.CornerRadius = 10;
        resolutionCard.Location = new Point(14, 42);
        resolutionCard.Name = "resolutionCard";
        resolutionCard.Size = new Size(97, 64);
        resolutionCard.SurfaceColor = UiPalette.SurfaceRaised;
        // resolutionTitle
        resolutionTitle.AutoSize = false;
        resolutionTitle.BackColor = Color.Transparent;
        resolutionTitle.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold, GraphicsUnit.Point);
        resolutionTitle.ForeColor = UiPalette.TextSecondary;
        resolutionTitle.Location = new Point(9, 7);
        resolutionTitle.Name = "resolutionTitle";
        resolutionTitle.Size = new Size(79, 18);
        resolutionTitle.Text = "分辨率";
        // metricResolution
        metricResolution.AutoSize = false;
        metricResolution.BackColor = Color.Transparent;
        metricResolution.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        metricResolution.ForeColor = UiPalette.TextPrimary;
        metricResolution.Location = new Point(8, 27);
        metricResolution.Name = "metricResolution";
        metricResolution.Size = new Size(81, 30);
        metricResolution.Text = "--";
        metricResolution.TextAlign = ContentAlignment.MiddleCenter;
        // bitrateCard
        bitrateCard.BorderColor = Color.Transparent;
        bitrateCard.Controls.Add(bitrateTitle);
        bitrateCard.Controls.Add(metricBitrate);
        bitrateCard.CornerRadius = 10;
        bitrateCard.Location = new Point(119, 42);
        bitrateCard.Name = "bitrateCard";
        bitrateCard.Size = new Size(97, 64);
        bitrateCard.SurfaceColor = UiPalette.SurfaceRaised;
        // bitrateTitle
        bitrateTitle.AutoSize = false;
        bitrateTitle.BackColor = Color.Transparent;
        bitrateTitle.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold, GraphicsUnit.Point);
        bitrateTitle.ForeColor = UiPalette.TextSecondary;
        bitrateTitle.Location = new Point(9, 7);
        bitrateTitle.Name = "bitrateTitle";
        bitrateTitle.Size = new Size(79, 18);
        bitrateTitle.Text = "码率";
        // metricBitrate
        metricBitrate.AutoSize = false;
        metricBitrate.BackColor = Color.Transparent;
        metricBitrate.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        metricBitrate.ForeColor = UiPalette.TextPrimary;
        metricBitrate.Location = new Point(8, 27);
        metricBitrate.Name = "metricBitrate";
        metricBitrate.Size = new Size(81, 30);
        metricBitrate.Text = "--";
        metricBitrate.TextAlign = ContentAlignment.MiddleCenter;
        // frameRateCard
        frameRateCard.BorderColor = Color.Transparent;
        frameRateCard.Controls.Add(frameRateTitle);
        frameRateCard.Controls.Add(metricFrameRate);
        frameRateCard.CornerRadius = 10;
        frameRateCard.Location = new Point(14, 116);
        frameRateCard.Name = "frameRateCard";
        frameRateCard.Size = new Size(97, 64);
        frameRateCard.SurfaceColor = UiPalette.SurfaceRaised;
        // frameRateTitle
        frameRateTitle.AutoSize = false;
        frameRateTitle.BackColor = Color.Transparent;
        frameRateTitle.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold, GraphicsUnit.Point);
        frameRateTitle.ForeColor = UiPalette.TextSecondary;
        frameRateTitle.Location = new Point(9, 7);
        frameRateTitle.Name = "frameRateTitle";
        frameRateTitle.Size = new Size(79, 18);
        frameRateTitle.Text = "帧率";
        // metricFrameRate
        metricFrameRate.AutoSize = false;
        metricFrameRate.BackColor = Color.Transparent;
        metricFrameRate.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        metricFrameRate.ForeColor = UiPalette.TextPrimary;
        metricFrameRate.Location = new Point(8, 27);
        metricFrameRate.Name = "metricFrameRate";
        metricFrameRate.Size = new Size(81, 30);
        metricFrameRate.Text = "--";
        metricFrameRate.TextAlign = ContentAlignment.MiddleCenter;
        // latencyCard
        latencyCard.BorderColor = Color.Transparent;
        latencyCard.Controls.Add(latencyTitle);
        latencyCard.Controls.Add(metricLatency);
        latencyCard.CornerRadius = 10;
        latencyCard.Location = new Point(119, 116);
        latencyCard.Name = "latencyCard";
        latencyCard.Size = new Size(97, 64);
        latencyCard.SurfaceColor = UiPalette.SurfaceRaised;
        // latencyTitle
        latencyTitle.AutoSize = false;
        latencyTitle.BackColor = Color.Transparent;
        latencyTitle.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold, GraphicsUnit.Point);
        latencyTitle.ForeColor = UiPalette.TextSecondary;
        latencyTitle.Location = new Point(9, 7);
        latencyTitle.Name = "latencyTitle";
        latencyTitle.Size = new Size(79, 18);
        latencyTitle.Text = "延迟";
        // metricLatency
        metricLatency.AutoSize = false;
        metricLatency.BackColor = Color.Transparent;
        metricLatency.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        metricLatency.ForeColor = UiPalette.TextPrimary;
        metricLatency.Location = new Point(8, 27);
        metricLatency.Name = "metricLatency";
        metricLatency.Size = new Size(81, 30);
        metricLatency.Text = "--";
        metricLatency.TextAlign = ContentAlignment.MiddleCenter;
        motionButton.Location = new Point(16, 174);
        motionButton.Name = "motionButton";
        motionButton.Size = new Size(198, 30);
        motionButton.Text = "设置";
        motionPanel.Location = Point.Empty;
        motionPanel.Name = "motionPanel";
        motionPanel.Size = new Size(596, 424);
        motionPanel.Visible = false;
        Controls.Add(motionPanel);
        playspaceCard.Controls.Add(playspaceStatusTitleHelp);
        playspaceCard.Controls.Add(inertiaTitleHelp);
        playspaceCard.Controls.Add(coordinateTitleHelp);
        playspaceCard.Controls.Add(motionButton);
        inertiaToggle.Location = new Point(158, 79);
        inertiaToggle.Size = new Size(56, 32);
        inertiaToggle.Name = "inertiaToggle";
        inertiaToggle.Text = "关闭";
        playspaceCard.Controls.Add(inertiaToggle);
        // playspaceCard
        playspaceCard.BorderColor = UiPalette.BorderSoft;
        playspaceCard.Controls.Add(playspaceTitle);
        playspaceCard.Controls.Add(playspaceStatusTitle);
        playspaceCard.Controls.Add(playspaceStatus);
        playspaceCard.Controls.Add(playspaceToggle);
        playspaceCard.Controls.Add(playspaceDivider);
        playspaceCard.Controls.Add(coordinateTitle);
        playspaceCard.Controls.Add(playspaceCoordinates);
        playspaceCard.Controls.Add(inertiaTitle);
        playspaceCard.CornerRadius = 14;
        playspaceCard.Location = new Point(366, 208);
        playspaceCard.Name = "playspaceCard";
        playspaceCard.Size = new Size(230, 216);
        playspaceCard.SurfaceColor = UiPalette.Surface;
        // playspaceTitle
        playspaceTitle.AutoSize = false;
        playspaceTitle.BackColor = Color.Transparent;
        playspaceTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        playspaceTitle.ForeColor = UiPalette.TextPrimary;
        playspaceTitle.Location = new Point(14, 10);
        playspaceTitle.Name = "playspaceTitle";
        playspaceTitle.Size = new Size(128, 24);
        playspaceTitle.Text = "空间拖拽";
        // playspaceStatusTitle
        playspaceStatusTitle.AutoSize = false;
        playspaceStatusTitle.BackColor = Color.Transparent;
        playspaceStatusTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        playspaceStatusTitle.ForeColor = UiPalette.TextPrimary;
        playspaceStatusTitle.Location = new Point(16, 44);
        playspaceStatusTitle.Name = "playspaceStatusTitle";
        playspaceStatusTitle.Size = new Size(44, 24);
        playspaceStatusTitle.Text = "总开关";
        // playspaceStatus
        playspaceStatus.AutoSize = false;
        playspaceStatus.BackColor = Color.Transparent;
        playspaceStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        playspaceStatus.ForeColor = UiPalette.TextPrimary;
        playspaceStatus.Location = new Point(16, 120);
        playspaceStatus.Name = "playspaceStatus";
        playspaceStatus.Size = new Size(86, 24);
        playspaceStatus.Text = "已关闭";
        playspaceStatus.Visible = false;
        // playspaceToggle
        playspaceToggle.Location = new Point(158, 39);
        playspaceToggle.Name = "playspaceToggle";
        playspaceToggle.Size = new Size(56, 32);
        playspaceToggle.Text = "关闭";
        // playspaceDivider
        playspaceDivider.BackColor = UiPalette.BorderSoft;
        playspaceDivider.Location = new Point(16, 116);
        playspaceDivider.Name = "playspaceDivider";
        playspaceDivider.Size = new Size(198, 1);
        // coordinateTitle
        coordinateTitle.AutoSize = false;
        coordinateTitle.BackColor = Color.Transparent;
        coordinateTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        coordinateTitle.ForeColor = UiPalette.TextPrimary;
        coordinateTitle.Location = new Point(16, 122);
        coordinateTitle.Name = "coordinateTitle";
        coordinateTitle.Size = new Size(60, 18);
        coordinateTitle.Text = "坐标";
        // playspaceCoordinates
        playspaceCoordinates.AutoSize = false;
        playspaceCoordinates.BackColor = Color.Transparent;
        playspaceCoordinates.Font = new Font("Microsoft YaHei UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point);
        playspaceCoordinates.ForeColor = UiPalette.TextPrimary;
        playspaceCoordinates.Location = new Point(16, 146);
        playspaceCoordinates.Name = "playspaceCoordinates";
        playspaceCoordinates.Size = new Size(198, 24);
        playspaceCoordinates.Text = "X --   Y --   Z --";
        playspaceCoordinates.TextAlign = ContentAlignment.MiddleLeft;
        // inertiaTitle
        inertiaTitle.AutoSize = false;
        inertiaTitle.BackColor = Color.Transparent;
        inertiaTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        inertiaTitle.ForeColor = UiPalette.TextPrimary;
        inertiaTitle.Location = new Point(16, 84);
        inertiaTitle.Name = "inertiaTitle";
        inertiaTitle.Size = new Size(104, 28);
        inertiaTitle.Text = "惯性与重力";
        // HomePageView
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = false;
        BackColor = UiPalette.Window;
        Controls.Add(deviceCard);
        Controls.Add(metricsCard);
        Controls.Add(playspaceCard);
        Name = "HomePageView";
        Size = new Size(596, 424);
        deviceCard.ResumeLayout(false);
        adbChip.ResumeLayout(false);
        phoneChip.ResumeLayout(false);
        videoChip.ResumeLayout(false);
        audioChip.ResumeLayout(false);
        controlChip.ResumeLayout(false);
        steamVrChip.ResumeLayout(false);
        metricsCard.ResumeLayout(false);
        resolutionCard.ResumeLayout(false);
        bitrateCard.ResumeLayout(false);
        frameRateCard.ResumeLayout(false);
        latencyCard.ResumeLayout(false);
        playspaceCard.ResumeLayout(false);
        ResumeLayout(false);
    }
}
