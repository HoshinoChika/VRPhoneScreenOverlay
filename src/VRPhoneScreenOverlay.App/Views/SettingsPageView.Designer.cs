#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class SettingsPageView
{
    private System.ComponentModel.IContainer components = null;
    private SurfacePanel card;
    private Label title;
    private Label status;
    private Label picoLabel;
    private Label picoStatus;
    private ModernToggle picoToggle;
    private System.Windows.Forms.Timer picoTimer;
    private Label keepAwakeLabel;
    private ModernToggle keepAwakeToggle;
    private Label unlockKeypadLabel;
    private ModernToggle unlockKeypadToggle;
    private Label autoOpenLabel;
    private ModernToggle autoOpenToggle;
    private Label screenOffLabel;
    private ModernToggle screenOffToggle;
    private Label minimizeLabel;
    private ModernToggle minimizeToggle;
    private Label steamVrLabel;
    private ModernToggle steamVrToggle;
    private ContextHelpIcon keepAwakeLabelHelp;
    private ContextHelpIcon unlockKeypadLabelHelp;
    private ContextHelpIcon autoOpenLabelHelp;
    private ContextHelpIcon screenOffLabelHelp;
    private ContextHelpIcon steamVrLabelHelp;
    private ContextHelpIcon minimizeLabelHelp;
    private ContextHelpIcon picoLabelHelp;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        keepAwakeLabelHelp = new ContextHelpIcon();
        unlockKeypadLabelHelp = new ContextHelpIcon();
        autoOpenLabelHelp = new ContextHelpIcon();
        screenOffLabelHelp = new ContextHelpIcon();
        steamVrLabelHelp = new ContextHelpIcon();
        minimizeLabelHelp = new ContextHelpIcon();
        picoLabelHelp = new ContextHelpIcon();

        components = new System.ComponentModel.Container();
        picoTimer = new System.Windows.Forms.Timer(components);
        picoLabel = new Label();
        picoStatus = new Label();
        picoToggle = new ModernToggle();
        card = new SurfacePanel();
        title = new Label();
        status = new Label();
        keepAwakeLabel = new Label();
        keepAwakeToggle = new ModernToggle();
        unlockKeypadLabel = new Label();
        unlockKeypadToggle = new ModernToggle();
        autoOpenLabel = new Label();
        autoOpenToggle = new ModernToggle();
        screenOffLabel = new Label();
        screenOffToggle = new ModernToggle();
        minimizeLabel = new Label();
        minimizeToggle = new ModernToggle();
        steamVrLabel = new Label();
        steamVrToggle = new ModernToggle();
        card.SuspendLayout();
        SuspendLayout();
        // keepAwakeLabelHelp
        keepAwakeLabelHelp.Name = "keepAwakeLabelHelp";
        keepAwakeLabelHelp.Location = new Point(470, 58);
        keepAwakeLabelHelp.Size = new Size(22, 22);
        keepAwakeLabelHelp.HelpText = UiHelpContent.KeepAwake;
        keepAwakeLabelHelp.TargetLabel = keepAwakeLabel;
        keepAwakeLabelHelp.AccessibleName = "抓握时唤醒手机说明";
        // unlockKeypadLabelHelp
        unlockKeypadLabelHelp.Name = "unlockKeypadLabelHelp";
        unlockKeypadLabelHelp.Location = new Point(470, 102);
        unlockKeypadLabelHelp.Size = new Size(22, 22);
        unlockKeypadLabelHelp.HelpText = UiHelpContent.Unlock;
        unlockKeypadLabelHelp.TargetLabel = unlockKeypadLabel;
        unlockKeypadLabelHelp.AccessibleName = "VR 解锁键盘说明";
        // autoOpenLabelHelp
        autoOpenLabelHelp.Name = "autoOpenLabelHelp";
        autoOpenLabelHelp.Location = new Point(470, 146);
        autoOpenLabelHelp.Size = new Size(22, 22);
        autoOpenLabelHelp.HelpText = UiHelpContent.AutoOpen;
        autoOpenLabelHelp.TargetLabel = autoOpenLabel;
        autoOpenLabelHelp.AccessibleName = "自动打开浮窗说明";
        // screenOffLabelHelp
        screenOffLabelHelp.Name = "screenOffLabelHelp";
        screenOffLabelHelp.Location = new Point(470, 190);
        screenOffLabelHelp.Size = new Size(22, 22);
        screenOffLabelHelp.HelpText = UiHelpContent.ScreenOff;
        screenOffLabelHelp.TargetLabel = screenOffLabel;
        screenOffLabelHelp.AccessibleName = "手机防误触说明";
        // steamVrLabelHelp
        steamVrLabelHelp.Name = "steamVrLabelHelp";
        steamVrLabelHelp.Location = new Point(470, 234);
        steamVrLabelHelp.Size = new Size(22, 22);
        steamVrLabelHelp.HelpText = UiHelpContent.SteamVr;
        steamVrLabelHelp.TargetLabel = steamVrLabel;
        steamVrLabelHelp.AccessibleName = "随 SteamVR 启动说明";
        // minimizeLabelHelp
        minimizeLabelHelp.Name = "minimizeLabelHelp";
        minimizeLabelHelp.Location = new Point(470, 278);
        minimizeLabelHelp.Size = new Size(22, 22);
        minimizeLabelHelp.HelpText = UiHelpContent.Minimize;
        minimizeLabelHelp.TargetLabel = minimizeLabel;
        minimizeLabelHelp.AccessibleName = "打开浮窗后最小化说明";
        // picoLabelHelp
        picoLabelHelp.Name = "picoLabelHelp";
        picoLabelHelp.Location = new Point(470, 322);
        picoLabelHelp.Size = new Size(22, 22);
        picoLabelHelp.HelpText = UiHelpContent.Pico;
        picoLabelHelp.TargetLabel = picoLabel;
        picoLabelHelp.AccessibleName = "PICO 麦克风延迟缓解说明";

        // card
        card.Location = new Point(0, 0);
        card.Size = new Size(596, 424);
        card.SurfaceColor = UiPalette.Surface;
        card.BorderColor = UiPalette.BorderSoft;
        card.CornerRadius = 14;
        card.Name = "card";
        // title
        title.Location = new Point(18, 12);
        title.Size = new Size(400, 30);
        title.Text = "设置";
        title.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        title.ForeColor = UiPalette.TextPrimary;
        title.BackColor = Color.Transparent;
        title.Name = "title";
        // keepAwakeLabel
        keepAwakeLabel.Location = new Point(18, 54);
        keepAwakeLabel.Size = new Size(440, 26);
        keepAwakeLabel.Text = "抓握时唤醒手机";
        keepAwakeLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        keepAwakeLabel.ForeColor = UiPalette.TextPrimary;
        keepAwakeLabel.BackColor = Color.Transparent;
        keepAwakeLabel.Name = "keepAwakeLabel";
        // keepAwakeToggle
        keepAwakeToggle.Location = new Point(512, 54);
        keepAwakeToggle.Size = new Size(68, 34);
        keepAwakeToggle.Name = "keepAwakeToggle";
        // unlockKeypadLabel
        unlockKeypadLabel.Location = new Point(18, 98);
        unlockKeypadLabel.Size = new Size(440, 26);
        unlockKeypadLabel.Text = "VR 解锁键盘";
        unlockKeypadLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        unlockKeypadLabel.ForeColor = UiPalette.TextPrimary;
        unlockKeypadLabel.BackColor = Color.Transparent;
        unlockKeypadLabel.Name = "unlockKeypadLabel";
        // unlockKeypadToggle
        unlockKeypadToggle.Location = new Point(512, 98);
        unlockKeypadToggle.Size = new Size(68, 34);
        unlockKeypadToggle.Name = "unlockKeypadToggle";
        // autoOpenLabel
        autoOpenLabel.Location = new Point(18, 142);
        autoOpenLabel.Size = new Size(440, 26);
        autoOpenLabel.Text = "自动打开浮窗";
        autoOpenLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        autoOpenLabel.ForeColor = UiPalette.TextPrimary;
        autoOpenLabel.BackColor = Color.Transparent;
        autoOpenLabel.Name = "autoOpenLabel";
        // autoOpenToggle
        autoOpenToggle.Location = new Point(512, 142);
        autoOpenToggle.Size = new Size(68, 34);
        autoOpenToggle.Name = "autoOpenToggle";
        // screenOffLabel
        screenOffLabel.Location = new Point(18, 186);
        screenOffLabel.Size = new Size(440, 26);
        screenOffLabel.Text = "手机防误触";
        screenOffLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        screenOffLabel.ForeColor = UiPalette.TextPrimary;
        screenOffLabel.BackColor = Color.Transparent;
        screenOffLabel.Name = "screenOffLabel";
        // screenOffToggle
        screenOffToggle.Location = new Point(512, 186);
        screenOffToggle.Size = new Size(68, 34);
        screenOffToggle.Name = "screenOffToggle";
        // steamVrLabel
        steamVrLabel.Location = new Point(18, 230);
        steamVrLabel.Size = new Size(440, 26);
        steamVrLabel.Text = "随 SteamVR 启动";
        steamVrLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        steamVrLabel.ForeColor = UiPalette.TextPrimary;
        steamVrLabel.BackColor = Color.Transparent;
        steamVrLabel.Name = "steamVrLabel";
        // steamVrToggle
        steamVrToggle.Location = new Point(512, 230);
        steamVrToggle.Size = new Size(68, 34);
        steamVrToggle.Name = "steamVrToggle";
        // minimizeLabel
        minimizeLabel.Location = new Point(18, 274);
        minimizeLabel.Size = new Size(440, 26);
        minimizeLabel.Text = "打开浮窗后最小化";
        minimizeLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        minimizeLabel.ForeColor = UiPalette.TextPrimary;
        minimizeLabel.BackColor = Color.Transparent;
        minimizeLabel.Name = "minimizeLabel";
        // minimizeToggle
        minimizeToggle.Location = new Point(512, 274);
        minimizeToggle.Size = new Size(68, 34);
        minimizeToggle.Name = "minimizeToggle";
        // picoLabel
        picoLabel.Location = new Point(18, 318);
        picoLabel.Size = new Size(440, 26);
        picoLabel.Text = "PICO 麦克风延迟缓解";
        picoLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
        picoLabel.ForeColor = UiPalette.TextPrimary;
        picoLabel.BackColor = Color.Transparent;
        picoLabel.Name = "picoLabel";
        picoToggle.Location = new Point(512, 318);
        picoToggle.Size = new Size(68, 34);
        picoToggle.Name = "picoToggle";
        picoStatus.Location = new Point(18, 356);
        picoStatus.Size = new Size(562, 26);
        picoStatus.ForeColor = UiPalette.TextSecondary;
        picoStatus.BackColor = Color.Transparent;
        picoStatus.Text = "已关闭";
        picoStatus.Name = "picoStatus";
        picoTimer.Interval = 1000;
        picoTimer.Tick += OnPicoStatusTick;
        picoTimer.Enabled = true;
        // status
        status.Location = new Point(18, 382);
        status.Size = new Size(562, 32);
        status.ForeColor = UiPalette.TextSecondary;
        status.BackColor = Color.Transparent;
        status.Name = "status";
        card.Controls.Add(keepAwakeLabelHelp);
        card.Controls.Add(unlockKeypadLabelHelp);
        card.Controls.Add(autoOpenLabelHelp);
        card.Controls.Add(screenOffLabelHelp);
        card.Controls.Add(steamVrLabelHelp);
        card.Controls.Add(minimizeLabelHelp);
        card.Controls.Add(picoLabelHelp);
        card.Controls.Add(picoLabel);
        card.Controls.Add(picoToggle);
        card.Controls.Add(picoStatus);
        card.Controls.Add(title);
        card.Controls.Add(status);
        card.Controls.Add(keepAwakeLabel);
        card.Controls.Add(keepAwakeToggle);
        card.Controls.Add(unlockKeypadLabel);
        card.Controls.Add(unlockKeypadToggle);
        card.Controls.Add(autoOpenLabel);
        card.Controls.Add(autoOpenToggle);
        card.Controls.Add(screenOffLabel);
        card.Controls.Add(screenOffToggle);
        card.Controls.Add(steamVrLabel);
        card.Controls.Add(steamVrToggle);
        card.Controls.Add(minimizeLabel);
        card.Controls.Add(minimizeToggle);
        // SettingsPageView
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiPalette.Window;
        Size = new Size(596, 424);
        Name = "SettingsPageView";
        Controls.Add(card);
        card.ResumeLayout(false);
        ResumeLayout(false);
    }
}
