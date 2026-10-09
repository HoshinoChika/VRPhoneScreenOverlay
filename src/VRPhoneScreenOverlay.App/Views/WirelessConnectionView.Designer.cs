#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class WirelessConnectionView
{
    private System.ComponentModel.IContainer components = null;
    private Label title;
    private ModernButton backButton;
    private WirelessPairingMethodSelectorControl methodSelector;
    private Panel qrPanel;
    private Panel usbPanel;
    private Label usbHelp;
    private ConnectionDeviceView deviceManager;
    private PictureBox qrImage;
    private Label qrHelp;
    private Panel codePanel;
    private Label codeHelp;
    private TextBox autoPairCode;
    private SurfacePanel autoPairCodeFrame;
    private ModernButton autoPairButton;
    private ModernButton fallbackButton;
    private Panel manualPanel;
    private Label help;
    private Label pairAddressLabel;
    private Label pairingCodeLabel;
    private Label connectionAddressLabel;
    private TextBox pairEndpoint;
    private SurfacePanel pairEndpointFrame;
    private TextBox pairCode;
    private SurfacePanel pairCodeFrame;
    private ModernButton pairButton;
    private TextBox connectEndpoint;
    private SurfacePanel connectEndpointFrame;
    private ModernButton connectButton;
    private Label status;
    private Label currentDevicesTitle;
    private ModernButton refreshButton;
    private ModernButton cancelButton;
    private ConnectionDeviceList deviceList;
    private ConnectionDeviceList historyDeviceList;
    private Label historyDevicesTitle;
    private Label currentEmpty;
    private Label historyEmpty;
    private SurfacePanel currentDevicesPanel;
    private SurfacePanel historyDevicesPanel;
    private Panel deviceManagerHost;

    private ContextHelpIcon titleHelp;
    private ContextHelpIcon currentDevicesTitleHelp;
    private ContextHelpIcon historyDevicesTitleHelp;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { qrImage.Image?.Dispose(); components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        titleHelp = new ContextHelpIcon();
        currentDevicesTitleHelp = new ContextHelpIcon();
        historyDevicesTitleHelp = new ContextHelpIcon();

        title = new Label();
        backButton = new ModernButton();
        methodSelector = new WirelessPairingMethodSelectorControl();
        qrPanel = new Panel();
        usbPanel = new Panel();
        usbHelp = new Label();
        deviceManager = new ConnectionDeviceView();
        qrImage = new PictureBox();
        qrHelp = new Label();
        codePanel = new Panel();
        codeHelp = new Label();
        autoPairCode = new TextBox();
        autoPairCodeFrame = new SurfacePanel();
        autoPairButton = new ModernButton();
        fallbackButton = new ModernButton();
        manualPanel = new Panel();
        help = new Label();
        pairAddressLabel = new Label();
        pairingCodeLabel = new Label();
        connectionAddressLabel = new Label();
        pairEndpoint = new TextBox();
        pairEndpointFrame = new SurfacePanel();
        pairCode = new TextBox();
        pairCodeFrame = new SurfacePanel();
        pairButton = new ModernButton();
        connectEndpoint = new TextBox();
        connectEndpointFrame = new SurfacePanel();
        connectButton = new ModernButton();
        status = new Label();
        currentDevicesTitle = new Label();
        refreshButton = new ModernButton();
        cancelButton = new ModernButton();
        deviceList = new ConnectionDeviceList();
        historyDeviceList = new ConnectionDeviceList();
        historyDevicesTitle = new Label();
        currentEmpty = new Label();
        historyEmpty = new Label();
        currentDevicesPanel = new SurfacePanel();
        historyDevicesPanel = new SurfacePanel();
        deviceManagerHost = new Panel();
        SuspendLayout();
        // titleHelp
        titleHelp.Name = "titleHelp";
        titleHelp.Location = new Point(118, 14);
        titleHelp.Size = new Size(22, 22);
        titleHelp.HelpText = UiHelpContent.Wireless;
        titleHelp.TargetLabel = title;
        titleHelp.AccessibleName = "连接管理说明";

        // title
        title.Location = new Point(18, 12);
        title.Size = new Size(94, 28);
        title.Name = "title";
        title.Text = "连接管理";
        title.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        title.ForeColor = UiPalette.TextSecondary;
        title.UseMnemonic = false;
        // backButton
        backButton.Location = new Point(480, 10);
        backButton.Size = new Size(96, 30);
        backButton.Name = "backButton";
        backButton.Text = "返回主页";
        // methodSelector
        methodSelector.Location = new Point(18, 48);
        methodSelector.Size = new Size(558, 34);
        methodSelector.Name = "methodSelector";
        // qrPanel
        qrPanel.Location = new Point(0, 94);
        qrPanel.Size = new Size(596, 110);
        qrPanel.Name = "qrPanel";
        // codePanel
        codePanel.Location = new Point(0, 94);
        codePanel.Size = new Size(596, 110);
        codePanel.Name = "codePanel";
        // manualPanel
        manualPanel.Location = new Point(0, 94);
        manualPanel.Size = new Size(596, 110);
        manualPanel.Name = "manualPanel";
        // qrImage
        qrImage.Location = new Point(18, 0);
        qrImage.Size = new Size(110, 110);
        qrImage.Name = "qrImage";
        qrImage.SizeMode = PictureBoxSizeMode.Zoom;
        qrImage.TabStop = false;
        qrImage.AccessibleName = "ADB 配对二维码";
        // qrHelp
        qrHelp.Location = new Point(144, 0);
        qrHelp.Size = new Size(432, 110);
        qrHelp.Name = "qrHelp";
        qrHelp.Text = "①手机与电脑处于同一个局域网（连接同一个路由器）。\r\n②开启手机的开发者选项，然后开启无线调试功能。\r\n③选择无线调试中的“使用二维码配对”，然后扫描左侧二维码。\r\n④在下方列表点击识别到的手机进行连接。";
        qrHelp.Font = new Font("Microsoft YaHei UI", 8.5F);
        qrHelp.ForeColor = UiPalette.TextSecondary;
        qrHelp.UseMnemonic = false;
        // codeHelp
        codeHelp.Location = new Point(18, 4);
        codeHelp.Size = new Size(558, 72);
        codeHelp.Name = "codeHelp";
        codeHelp.Text = "①手机与电脑处于同一个局域网（连接同一个路由器）。\r\n②开启手机的开发者选项，然后开启无线调试功能。\r\n③选择“使用配对码配对设备”，保持窗口打开，输入六位配对码。\r\n④点击“配对并连接”，再在下方列表点击识别到的手机进行连接。";
        codeHelp.Font = new Font("Microsoft YaHei UI", 8.5F);
        codeHelp.ForeColor = UiPalette.TextSecondary;
        codeHelp.UseMnemonic = false;
        // autoPairCode
        autoPairCode.Location = new Point(8, 4);
        autoPairCode.Size = new Size(230, 20);
        autoPairCode.Name = "autoPairCode";
        autoPairCodeFrame.Location = new Point(18, 80);
        autoPairCodeFrame.Size = new Size(246, 28);
        autoPairCodeFrame.Name = "autoPairCodeFrame";
        autoPairCodeFrame.SurfaceColor = UiPalette.Window;
        autoPairCodeFrame.BorderColor = UiPalette.Border;
        autoPairCodeFrame.CornerRadius = 8;
        autoPairCodeFrame.Controls.Add(autoPairCode);
        autoPairCode.BackColor = UiPalette.Window;
        autoPairCode.ForeColor = UiPalette.TextPrimary;
        autoPairCode.BorderStyle = BorderStyle.None;
        autoPairCode.MaxLength = 6;
        autoPairCode.PlaceholderText = "六位配对码";
        autoPairCode.AccessibleName = "六位配对码";
        autoPairCode.UseSystemPasswordChar = true;
        // autoPairButton
        autoPairButton.Location = new Point(276, 78);
        autoPairButton.Size = new Size(156, 30);
        autoPairButton.Name = "autoPairButton";
        autoPairButton.Text = "配对并连接";
        // fallbackButton
        fallbackButton.Location = new Point(444, 82);
        fallbackButton.Size = new Size(132, 26);
        fallbackButton.Name = "fallbackButton";
        fallbackButton.Text = "手动连接";
        fallbackButton.Visible = false;
        // help
        help.Location = new Point(18, 0);
        help.Size = new Size(244, 110);
        help.Name = "help";
        help.Text = "①手机与电脑连接同一路由器。\r\n②手机开启开发者选项和无线调试。\r\n③未配对：读取配对 IP、端口和六位配对码，在右侧配对。\r\n④已配对：从无线调试主页读取连接 IP 和端口后连接。";
        help.Font = new Font("Microsoft YaHei UI", 8F);
        help.ForeColor = UiPalette.TextSecondary;
        help.UseMnemonic = false;
        // pairEndpoint
        pairEndpoint.Location = new Point(8, 4);
        pairEndpoint.Size = new Size(220, 20);
        pairEndpoint.Name = "pairEndpoint";
        pairEndpointFrame.Location = new Point(340, 0);
        pairEndpointFrame.Size = new Size(236, 28);
        pairEndpointFrame.Name = "pairEndpointFrame";
        pairEndpointFrame.SurfaceColor = UiPalette.Window;
        pairEndpointFrame.BorderColor = UiPalette.Border;
        pairEndpointFrame.CornerRadius = 8;
        pairEndpointFrame.Controls.Add(pairEndpoint);
        pairEndpoint.BackColor = UiPalette.Window;
        pairEndpoint.ForeColor = UiPalette.TextPrimary;
        pairEndpoint.BorderStyle = BorderStyle.None;
        pairEndpoint.MaxLength = 80;
        pairEndpoint.PlaceholderText = "192.168.1.20:配对端口";
        pairEndpoint.AccessibleName = "192.168.1.20:配对端口";
        // pairCode
        pairCode.Location = new Point(8, 4);
        pairCode.Size = new Size(84, 20);
        pairCode.Name = "pairCode";
        pairCodeFrame.Location = new Point(340, 40);
        pairCodeFrame.Size = new Size(100, 28);
        pairCodeFrame.Name = "pairCodeFrame";
        pairCodeFrame.SurfaceColor = UiPalette.Window;
        pairCodeFrame.BorderColor = UiPalette.Border;
        pairCodeFrame.CornerRadius = 8;
        pairCodeFrame.Controls.Add(pairCode);
        pairCode.BackColor = UiPalette.Window;
        pairCode.ForeColor = UiPalette.TextPrimary;
        pairCode.BorderStyle = BorderStyle.None;
        pairCode.MaxLength = 6;
        pairCode.PlaceholderText = "六位配对码";
        pairCode.AccessibleName = "六位配对码";
        pairCode.UseSystemPasswordChar = true;
        // pairButton
        pairButton.Location = new Point(452, 38);
        pairButton.Size = new Size(124, 30);
        pairButton.Name = "pairButton";
        pairButton.Text = "配对并连接";
        // connectEndpoint
        connectEndpoint.Location = new Point(8, 4);
        connectEndpoint.Size = new Size(112, 20);
        connectEndpoint.Name = "connectEndpoint";
        connectEndpointFrame.Location = new Point(340, 80);
        connectEndpointFrame.Size = new Size(128, 28);
        connectEndpointFrame.Name = "connectEndpointFrame";
        connectEndpointFrame.SurfaceColor = UiPalette.Window;
        connectEndpointFrame.BorderColor = UiPalette.Border;
        connectEndpointFrame.CornerRadius = 8;
        connectEndpointFrame.Controls.Add(connectEndpoint);
        connectEndpoint.BackColor = UiPalette.Window;
        connectEndpoint.ForeColor = UiPalette.TextPrimary;
        connectEndpoint.BorderStyle = BorderStyle.None;
        connectEndpoint.MaxLength = 80;
        connectEndpoint.PlaceholderText = "192.168.1.20:连接端口";
        connectEndpoint.AccessibleName = "192.168.1.20:连接端口";
        // connectButton
        connectButton.Location = new Point(480, 78);
        connectButton.Size = new Size(96, 30);
        connectButton.Name = "connectButton";
        connectButton.Text = "连接";
        // status
        status.Location = new Point(18, 208);
        status.Size = new Size(414, 24);
        status.Name = "status";
        status.Text = "";
        status.Font = new Font("Microsoft YaHei UI", 8.5F);
        status.ForeColor = UiPalette.TextSecondary;
        status.UseMnemonic = false;
        status.AutoEllipsis = true;
        // currentDevicesTitle
        currentDevicesTitle.Location = new Point(12, 10);
        currentDevicesTitle.Size = new Size(246, 22);
        currentDevicesTitle.Name = "currentDevicesTitle";
        currentDevicesTitle.Text = "当前设备";
        currentDevicesTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        currentDevicesTitle.ForeColor = UiPalette.TextSecondary;
        currentDevicesTitle.UseMnemonic = false;
        // currentDevicesTitleHelp
        currentDevicesTitleHelp.Name = "currentDevicesTitleHelp";
        currentDevicesTitleHelp.Location = new Point(90, 10);
        currentDevicesTitleHelp.Size = new Size(22, 22);
        currentDevicesTitleHelp.HelpText = UiHelpContent.CurrentDevices;
        currentDevicesTitleHelp.TargetLabel = currentDevicesTitle;
        currentDevicesTitleHelp.AccessibleName = "当前设备说明";
        // refreshButton
        refreshButton.Location = new Point(352, 10);
        refreshButton.Size = new Size(116, 30);
        refreshButton.Name = "refreshButton";
        refreshButton.Text = "刷新设备";
        // cancelButton
        cancelButton.Location = new Point(444, 208);
        cancelButton.Size = new Size(132, 26);
        cancelButton.Name = "cancelButton";
        cancelButton.Text = "取消配对";
        cancelButton.Enabled = false;
        cancelButton.Visible = false;
        // deviceList
        deviceList.Location = new Point(12, 38);
        deviceList.Size = new Size(246, 128);
        deviceList.Name = "deviceList";
        deviceList.Font = new Font("Microsoft YaHei UI", 8.5F);
        deviceList.BackColor = UiPalette.SurfaceRaised;
        deviceList.ForeColor = UiPalette.TextPrimary;
        deviceList.BorderStyle = BorderStyle.None;
        deviceList.HorizontalScrollbar = false;
        deviceList.IntegralHeight = false;
        // currentDevicesPanel
        currentDevicesPanel.Location = new Point(18, 234);
        currentDevicesPanel.Size = new Size(270, 178);
        currentDevicesPanel.Name = "currentDevicesPanel";
        currentDevicesPanel.SurfaceColor = UiPalette.SurfaceRaised;
        currentDevicesPanel.BorderColor = UiPalette.BorderSoft;
        currentDevicesPanel.CornerRadius = 10;
        currentEmpty.Location = new Point(12, 38);
        currentEmpty.Size = new Size(246, 128);
        currentEmpty.Name = "currentEmpty";
        currentEmpty.Text = "暂无当前方式可识别的设备";
        currentEmpty.TextAlign = ContentAlignment.MiddleCenter;
        currentEmpty.ForeColor = UiPalette.TextSecondary;
        currentDevicesPanel.Controls.Add(currentDevicesTitle);
        currentDevicesPanel.Controls.Add(currentDevicesTitleHelp);
        currentDevicesPanel.Controls.Add(deviceList);
        currentDevicesPanel.Controls.Add(currentEmpty);
        // historyDevicesPanel
        historyDevicesPanel.Location = new Point(306, 234);
        historyDevicesPanel.Size = new Size(270, 178);
        historyDevicesPanel.Name = "historyDevicesPanel";
        historyDevicesPanel.SurfaceColor = UiPalette.SurfaceRaised;
        historyDevicesPanel.BorderColor = UiPalette.BorderSoft;
        historyDevicesPanel.CornerRadius = 10;
        historyDevicesTitle.Location = new Point(12, 10);
        historyDevicesTitle.Size = new Size(246, 22);
        historyDevicesTitle.Name = "historyDevicesTitle";
        historyDevicesTitle.Text = "历史设备";
        historyDevicesTitle.ForeColor = UiPalette.TextSecondary;
        historyDevicesTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        historyDevicesTitle.UseMnemonic = false;
        // historyDevicesTitleHelp
        historyDevicesTitleHelp.Name = "historyDevicesTitleHelp";
        historyDevicesTitleHelp.Location = new Point(90, 10);
        historyDevicesTitleHelp.Size = new Size(22, 22);
        historyDevicesTitleHelp.HelpText = UiHelpContent.HistoryDevices;
        historyDevicesTitleHelp.TargetLabel = historyDevicesTitle;
        historyDevicesTitleHelp.AccessibleName = "历史设备说明";
        historyDeviceList.Location = new Point(12, 38);
        historyDeviceList.Size = new Size(246, 128);
        historyDeviceList.Name = "historyDeviceList";
        historyDeviceList.Font = new Font("Microsoft YaHei UI", 8.5F);
        historyDeviceList.BackColor = UiPalette.SurfaceRaised;
        historyDeviceList.ForeColor = UiPalette.TextPrimary;
        historyEmpty.Location = new Point(12, 38);
        historyEmpty.Size = new Size(246, 128);
        historyEmpty.Name = "historyEmpty";
        historyEmpty.Text = "暂无历史连接记录";
        historyEmpty.TextAlign = ContentAlignment.MiddleCenter;
        historyEmpty.ForeColor = UiPalette.TextSecondary;
        historyDevicesPanel.Controls.Add(historyDevicesTitle);
        historyDevicesPanel.Controls.Add(historyDevicesTitleHelp);
        historyDevicesPanel.Controls.Add(historyDeviceList);
        historyDevicesPanel.Controls.Add(historyEmpty);
        // deviceManagerHost
        deviceManagerHost.Location = new Point(0, 46);
        deviceManagerHost.Size = new Size(596, 378);
        deviceManagerHost.Name = "deviceManagerHost";
        deviceManagerHost.BackColor = UiPalette.Surface;
        deviceManagerHost.Visible = false;
        // usbPanel
        usbPanel.Location = new Point(0, 94);
        usbPanel.Size = new Size(596, 110);
        usbPanel.Name = "usbPanel";
        // usbHelp
        usbHelp.Location = new Point(18, 6);
        usbHelp.Size = new Size(558, 98);
        usbHelp.Name = "usbHelp";
        usbHelp.Text = "①开启手机的开发者选项。\r\n②在开发者选项内开启 USB 调试功能。\r\n③使用数据线连接电脑和手机，并同意 USB 调试请求。\r\n④在下方列表点击识别到的手机进行连接。";
        usbHelp.Font = new Font("Microsoft YaHei UI", 9F);
        usbHelp.ForeColor = UiPalette.TextSecondary;
        usbHelp.UseMnemonic = false;
        usbPanel.Controls.Add(usbHelp);
        // deviceManager
        deviceManager.Location = new Point(98, 42);
        deviceManager.Size = new Size(400, 282);
        deviceManager.Name = "deviceManager";
        deviceManager.Visible = true;
        deviceManagerHost.Controls.Add(deviceManager);
        qrImage.TabIndex = 0;
        qrPanel.Controls.Add(qrImage);
        qrHelp.TabIndex = 1;
        qrPanel.Controls.Add(qrHelp);
        codeHelp.TabIndex = 0;
        codePanel.Controls.Add(codeHelp);
        autoPairCode.TabIndex = 2;
        codePanel.Controls.Add(autoPairCodeFrame);
        autoPairButton.TabIndex = 3;
        codePanel.Controls.Add(autoPairButton);
        fallbackButton.TabIndex = 4;
        codePanel.Controls.Add(fallbackButton);
        help.TabIndex = 0;
        manualPanel.Controls.Add(help);
        // manual address labels
        pairAddressLabel.Location = new Point(276, 4);
        pairAddressLabel.Size = new Size(62, 24);
        pairAddressLabel.Name = "pairAddressLabel";
        pairAddressLabel.Text = "配对地址";
        pairAddressLabel.Font = new Font("Microsoft YaHei UI", 8F);
        pairAddressLabel.ForeColor = UiPalette.TextSecondary;
        pairingCodeLabel.Location = new Point(276, 44);
        pairingCodeLabel.Size = new Size(62, 24);
        pairingCodeLabel.Name = "pairingCodeLabel";
        pairingCodeLabel.Text = "配对码";
        pairingCodeLabel.Font = new Font("Microsoft YaHei UI", 8F);
        pairingCodeLabel.ForeColor = UiPalette.TextSecondary;
        connectionAddressLabel.Location = new Point(276, 84);
        connectionAddressLabel.Size = new Size(62, 24);
        connectionAddressLabel.Name = "connectionAddressLabel";
        connectionAddressLabel.Text = "连接地址";
        connectionAddressLabel.Font = new Font("Microsoft YaHei UI", 8F);
        connectionAddressLabel.ForeColor = UiPalette.TextSecondary;
        manualPanel.Controls.Add(pairAddressLabel);
        manualPanel.Controls.Add(pairingCodeLabel);
        manualPanel.Controls.Add(connectionAddressLabel);
        pairEndpoint.TabIndex = 3;
        manualPanel.Controls.Add(pairEndpointFrame);
        pairCode.TabIndex = 4;
        manualPanel.Controls.Add(pairCodeFrame);
        pairButton.TabIndex = 5;
        manualPanel.Controls.Add(pairButton);
        connectEndpoint.TabIndex = 7;
        manualPanel.Controls.Add(connectEndpointFrame);
        connectButton.TabIndex = 8;
        manualPanel.Controls.Add(connectButton);
        title.TabIndex = 0;
        Controls.Add(titleHelp);
        Controls.Add(title);
        backButton.TabIndex = 1;
        Controls.Add(backButton);
        methodSelector.TabIndex = 2;
        Controls.Add(methodSelector);
        Controls.Add(usbPanel);
        qrPanel.TabIndex = 3;
        Controls.Add(qrPanel);
        codePanel.TabIndex = 7;
        Controls.Add(codePanel);
        manualPanel.TabIndex = 13;
        Controls.Add(manualPanel);
        status.TabIndex = 24;
        Controls.Add(status);
        currentDevicesTitle.TabIndex = 25;

        refreshButton.TabIndex = 26;
        Controls.Add(refreshButton);
        cancelButton.TabIndex = 27;
        Controls.Add(cancelButton);
        deviceList.TabIndex = 28;
        Controls.Add(currentDevicesPanel);
        Controls.Add(historyDevicesPanel);
        Controls.Add(deviceManagerHost);
        codePanel.Visible = false;
        manualPanel.Visible = false;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiPalette.Surface;
        Font = new Font("Microsoft YaHei UI", 9F);
        Name = "WirelessConnectionView";
        Size = new Size(596, 424);
        ResumeLayout(false);
        PerformLayout();
    }
}
