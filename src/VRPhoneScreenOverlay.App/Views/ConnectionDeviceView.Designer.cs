#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class ConnectionDeviceView
{
    private System.ComponentModel.IContainer components = null;
    private Label title;
    private SurfacePanel dialogSurface;
    private SurfacePanel renameFrame;
    private Label details;
    private Label autoConnectLabel;
    private ModernToggle autoConnectToggle;
    private Label renameLabel;
    private TextBox renameTextBox;
    private Label renameHint;
    private ModernButton connectButton;
    private ModernButton saveButton;
    private ModernButton forgetButton;
    private ModernButton cancelButton;
    private Label status;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        title = new Label();
        dialogSurface = new SurfacePanel();
        renameFrame = new SurfacePanel();
        details = new Label();
        autoConnectLabel = new Label();
        autoConnectToggle = new ModernToggle();
        renameLabel = new Label();
        renameTextBox = new TextBox();
        renameHint = new Label();
        connectButton = new ModernButton();
        saveButton = new ModernButton();
        forgetButton = new ModernButton();
        cancelButton = new ModernButton();
        status = new Label();
        SuspendLayout();
        // title
        title.Location = new Point(18, 14);
        title.Size = new Size(364, 24);
        title.Name = "title";
        title.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        title.ForeColor = UiPalette.TextPrimary;
        title.AutoEllipsis = true;
        title.UseMnemonic = false;
        // details
        details.Location = new Point(18, 44);
        details.Size = new Size(364, 38);
        details.Name = "details";
        details.Font = new Font("Microsoft YaHei UI", 8.5F);
        details.ForeColor = UiPalette.TextSecondary;
        details.UseMnemonic = false;
        // autoConnectLabel
        autoConnectLabel.Location = new Point(18, 86);
        autoConnectLabel.Size = new Size(260, 24);
        autoConnectLabel.Name = "autoConnectLabel";
        autoConnectLabel.Text = "自动连接此设备";
        autoConnectLabel.ForeColor = UiPalette.TextPrimary;
        autoConnectLabel.TextAlign = ContentAlignment.MiddleLeft;
        // autoConnectToggle
        autoConnectToggle.Location = new Point(310, 86);
        autoConnectToggle.Size = new Size(72, 24);
        autoConnectToggle.Name = "autoConnectToggle";
        autoConnectToggle.CausesValidation = false;
        // renameLabel
        renameLabel.Location = new Point(18, 122);
        renameLabel.Size = new Size(364, 18);
        renameLabel.Name = "renameLabel";
        renameLabel.Text = "设备名称";
        renameLabel.ForeColor = UiPalette.TextPrimary;
        // renameTextBox
        renameTextBox.Location = new Point(8, 4);
        renameTextBox.Size = new Size(348, 20);
        renameTextBox.Name = "renameTextBox";
        renameFrame.Location = new Point(18, 146);
        renameFrame.Size = new Size(364, 28);
        renameFrame.Name = "renameFrame";
        renameFrame.SurfaceColor = UiPalette.Window;
        renameFrame.BorderColor = UiPalette.Border;
        renameFrame.CornerRadius = 8;
        renameFrame.Controls.Add(renameTextBox);
        renameTextBox.MaxLength = 40;
        renameTextBox.BackColor = UiPalette.Window;
        renameTextBox.ForeColor = UiPalette.TextPrimary;
        renameTextBox.BorderStyle = BorderStyle.None;
        renameTextBox.PlaceholderText = "输入设备名称";
        renameTextBox.AccessibleName = "重命名设备，最多20个汉字字母数字";
        // renameHint
        renameHint.Location = new Point(18, 178);
        renameHint.Size = new Size(364, 34);
        renameHint.Name = "renameHint";
        renameHint.Text = "汉字、英文字母、数字 · 1–20 字\r\n修改后点击保存配置";
        renameHint.Font = new Font("Microsoft YaHei UI", 8F);
        renameHint.ForeColor = UiPalette.TextSecondary;
        renameHint.UseMnemonic = false;
        // saveButton
        saveButton.Location = new Point(98, 214);
        saveButton.Size = new Size(88, 30);
        saveButton.Name = "saveButton";
        saveButton.Text = "保存配置";
        saveButton.Primary = true;
        saveButton.CausesValidation = false;
        // connectButton
        connectButton.Location = new Point(196, 214);
        connectButton.Size = new Size(88, 30);
        connectButton.Name = "connectButton";
        connectButton.Text = "连接";
        connectButton.Tone = UiButtonTone.Success;
        connectButton.CausesValidation = false;
        // forgetButton
        forgetButton.Location = new Point(196, 214);
        forgetButton.Size = new Size(88, 30);
        forgetButton.Name = "forgetButton";
        forgetButton.Text = "忘记";
        forgetButton.Tone = UiButtonTone.Danger;
        forgetButton.CausesValidation = false;
        // cancelButton
        cancelButton.Location = new Point(294, 214);
        cancelButton.Size = new Size(88, 30);
        cancelButton.Name = "cancelButton";
        cancelButton.Text = "取消";
        cancelButton.CausesValidation = false;
        // status
        status.Location = new Point(18, 252);
        status.Size = new Size(364, 20);
        status.Name = "status";
        status.Font = new Font("Microsoft YaHei UI", 8F);
        status.ForeColor = UiPalette.TextSecondary;
        status.AutoEllipsis = true;
        status.UseMnemonic = false;
        dialogSurface.Controls.Add(title);
        dialogSurface.Controls.Add(details);
        dialogSurface.Controls.Add(autoConnectLabel);
        dialogSurface.Controls.Add(autoConnectToggle);
        dialogSurface.Controls.Add(renameLabel);
        dialogSurface.Controls.Add(renameFrame);
        dialogSurface.Controls.Add(renameHint);
        dialogSurface.Controls.Add(connectButton);
        dialogSurface.Controls.Add(saveButton);
        dialogSurface.Controls.Add(forgetButton);
        dialogSurface.Controls.Add(cancelButton);
        dialogSurface.Controls.Add(status);
        dialogSurface.Location = Point.Empty;
        dialogSurface.Size = new Size(400, 282);
        dialogSurface.Name = "dialogSurface";
        dialogSurface.SurfaceColor = UiPalette.SurfaceRaised;
        dialogSurface.BorderColor = UiPalette.HelpBorder;
        dialogSurface.CornerRadius = 12;
        Controls.Add(dialogSurface);
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiPalette.Surface;
        Font = new Font("Microsoft YaHei UI", 9F);
        Name = "ConnectionDeviceView";
        Size = new Size(400, 282);
        ResumeLayout(false);
        PerformLayout();
    }
}
