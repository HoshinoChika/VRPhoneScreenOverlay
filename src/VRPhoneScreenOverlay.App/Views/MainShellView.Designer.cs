#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class MainShellView
{
    private System.ComponentModel.IContainer components = null;
    private Panel sidebar = null!;
    private Panel divider = null!;
    private Panel pageHost = null!;
    private SurfacePanel titleBar = null!;
    private ApplicationIconView appMark = null!;
    private Label productTitle = null!;
    private WindowControlButton minimizeButton = null!;
    private WindowControlButton closeButton = null!;
    private Panel titleDivider = null!;
    private CloseConfirmationView closeConfirmation = null!;
    private ContextHelpPopup contextHelp = null!;
    private NavigationButton homeNavigation = null!;
    private NavigationButton settingsNavigation = null!;
    private NavigationButton featuresNavigation = null!;
    private NavigationButton videoNavigation = null!;
    private NavigationButton aboutNavigation = null!;

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
        closeConfirmation = new CloseConfirmationView();
        contextHelp = new ContextHelpPopup();
        contextHelp.Name = "contextHelp";
        contextHelp.Size = new Size(390, 140);
        contextHelp.Visible = false;
        sidebar = new Panel();
        divider = new Panel();
        homeNavigation = new NavigationButton();
        settingsNavigation = new NavigationButton();
        featuresNavigation = new NavigationButton();
        videoNavigation = new NavigationButton();
        aboutNavigation = new NavigationButton();
        pageHost = new Panel();
        titleBar = new SurfacePanel();
        appMark = new ApplicationIconView();
        productTitle = new Label();
        minimizeButton = new WindowControlButton();
        closeButton = new WindowControlButton();
        titleDivider = new Panel();
        sidebar.SuspendLayout();
        titleBar.SuspendLayout();
        SuspendLayout();
        // sidebar
        sidebar.AutoSize = false;
        sidebar.BackColor = UiPalette.Sidebar;
        sidebar.Controls.Add(homeNavigation);
        sidebar.Controls.Add(settingsNavigation);
        sidebar.Controls.Add(featuresNavigation);
        sidebar.Controls.Add(videoNavigation);
        sidebar.Controls.Add(aboutNavigation);
        sidebar.Controls.Add(divider);
        sidebar.Location = new Point(2, 42);
        sidebar.Name = "sidebar";
        sidebar.Size = new Size(126, 458);
        // divider
        divider.BackColor = UiPalette.BorderSoft;
        divider.Location = new Point(125, 0);
        divider.Name = "divider";
        divider.Size = new Size(1, 458);
        // homeNavigation
        homeNavigation.Icon = UiIcons.Home;
        homeNavigation.Location = new Point(12, 14);
        homeNavigation.Name = "homeNavigation";
        homeNavigation.Size = new Size(104, 44);
        homeNavigation.Text = "主页";
        // settingsNavigation
        settingsNavigation.Icon = UiIcons.Settings;
        settingsNavigation.Location = new Point(12, 170);
        settingsNavigation.Name = "settingsNavigation";
        settingsNavigation.Size = new Size(104, 44);
        settingsNavigation.Text = "设置";
        // featuresNavigation
        featuresNavigation.Icon = UiIcons.Puzzle;
        featuresNavigation.Location = new Point(12, 66);
        featuresNavigation.Name = "featuresNavigation";
        featuresNavigation.Size = new Size(104, 44);
        featuresNavigation.Text = "功能";
        // videoNavigation
        videoNavigation.Icon = UiIcons.Video;
        videoNavigation.Location = new Point(12, 118);
        videoNavigation.Name = "videoNavigation";
        videoNavigation.Size = new Size(104, 44);
        videoNavigation.Text = "画面";
        // aboutNavigation
        aboutNavigation.Icon = UiIcons.Info;
        aboutNavigation.Location = new Point(12, 402);
        aboutNavigation.Name = "aboutNavigation";
        aboutNavigation.Size = new Size(104, 44);
        aboutNavigation.Text = "关于";
        // pageHost
        pageHost.AutoSize = false;
        pageHost.BackColor = UiPalette.Window;
        pageHost.Location = new Point(128, 42);
        pageHost.Name = "pageHost";
        pageHost.Size = new Size(630, 458);
        // titleBar
        titleBar.BackColor = Color.Transparent;
        titleBar.BorderColor = UiPalette.Header;
        titleBar.Controls.Add(appMark);
        titleBar.Controls.Add(productTitle);
        titleBar.Controls.Add(minimizeButton);
        titleBar.Controls.Add(closeButton);
        titleBar.CornerRadius = 11;
        titleBar.Location = new Point(2, 2);
        titleBar.Name = "titleBar";
        titleBar.Size = new Size(756, 40);
        titleBar.SurfaceColor = UiPalette.Header;
        // appMark
        appMark.Location = new Point(13, 7);
        appMark.Name = "appMark";
        appMark.Size = new Size(26, 26);
        // productTitle
        productTitle.AutoSize = false;
        productTitle.BackColor = Color.Transparent;
        productTitle.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        productTitle.ForeColor = UiPalette.TextPrimary;
        productTitle.Location = new Point(48, 0);
        productTitle.Name = "productTitle";
        productTitle.Size = new Size(220, 40);
        productTitle.Text = "VRPhoneScreen Overlay";
        productTitle.TextAlign = ContentAlignment.MiddleLeft;
        productTitle.UseMnemonic = false;
        // minimizeButton
        minimizeButton.AccessibleName = "最小化";
        minimizeButton.Icon = UiIcons.Minimize;
        minimizeButton.Location = new Point(669, 0);
        minimizeButton.Name = "minimizeButton";
        minimizeButton.Size = new Size(44, 40);
        // closeButton
        closeButton.AccessibleName = "关闭窗口";
        closeButton.DangerOnHover = true;
        closeButton.Icon = UiIcons.Close;
        closeButton.Location = new Point(713, 0);
        closeButton.Name = "closeButton";
        closeButton.Size = new Size(43, 40);
        // titleDivider
        titleDivider.BackColor = UiPalette.BorderSoft;
        titleDivider.Location = new Point(1, 41);
        titleDivider.Name = "titleDivider";
        titleDivider.Size = new Size(758, 1);
        // MainShellView
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = false;
        BackColor = UiPalette.Window;
        closeConfirmation.Location = new Point(1, 42);
        closeConfirmation.Size = new Size(758, 459);
        closeConfirmation.Name = "closeConfirmation";
        closeConfirmation.Visible = false;
        Controls.Add(closeConfirmation);
        Controls.Add(contextHelp);
        Controls.Add(pageHost);
        Controls.Add(sidebar);
        Controls.Add(titleBar);
        Controls.Add(titleDivider);
        Name = "MainShellView";
        Size = new Size(760, 502);
        sidebar.ResumeLayout(false);
        titleBar.ResumeLayout(false);
        ResumeLayout(false);
    }
}
