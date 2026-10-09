#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class CloseConfirmationView
{
    private System.ComponentModel.IContainer components = null;
    private SurfacePanel card;
    private Label title;
    private Label description;
    private TextBox releaseNotes;
    private ModernButton minimizeButton;
    private ModernButton exitButton;
    private ModernButton cancelButton;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        card = new SurfacePanel();
        title = new Label();
        description = new Label();
        releaseNotes = new TextBox();
        minimizeButton = new ModernButton();
        exitButton = new ModernButton();
        cancelButton = new ModernButton();
        card.SuspendLayout();
        SuspendLayout();
        // card
        card.Location = new Point(80, 78);
        card.Size = new Size(596, 304);
        card.SurfaceColor = UiPalette.SurfaceRaised;
        card.BorderColor = UiPalette.Accent;
        card.CornerRadius = 14;
        card.Name = "card";
        // title
        title.Location = new Point(28, 28);
        title.Size = new Size(540, 42);
        title.Text = "关闭窗口";
        title.Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold);
        title.ForeColor = UiPalette.TextPrimary;
        title.BackColor = Color.Transparent;
        title.Name = "title";
        // description
        description.Location = new Point(28, 86);
        description.Size = new Size(540, 34);
        description.Text = "最小化保留浮窗，退出关闭浮窗。";
        description.Font = new Font("Microsoft YaHei UI", 9.5F);
        description.ForeColor = UiPalette.TextSecondary;
        description.BackColor = Color.Transparent;
        description.Name = "description";
        // releaseNotes
        releaseNotes.Location = new Point(28, 105);
        releaseNotes.Size = new Size(540, 140);
        releaseNotes.Multiline = true;
        releaseNotes.ReadOnly = true;
        releaseNotes.ScrollBars = ScrollBars.Vertical;
        releaseNotes.BorderStyle = BorderStyle.None;
        releaseNotes.BackColor = UiPalette.SurfaceRaised;
        releaseNotes.ForeColor = UiPalette.TextPrimary;
        releaseNotes.Font = new Font("Microsoft YaHei UI", 10F);
        releaseNotes.AccessibleName = "更新内容";
        releaseNotes.Visible = false;
        releaseNotes.Name = "releaseNotes";
        // minimizeButton
        minimizeButton.Location = new Point(28, 142);
        minimizeButton.Size = new Size(260, 62);
        minimizeButton.Text = "最小化到托盘";
        minimizeButton.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        minimizeButton.Primary = true;
        minimizeButton.Name = "minimizeButton";
        minimizeButton.TabIndex = 0;
        // exitButton
        exitButton.Location = new Point(308, 142);
        exitButton.Size = new Size(260, 62);
        exitButton.Text = "退出程序";
        exitButton.Tone = UiButtonTone.Danger;
        exitButton.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        exitButton.Name = "exitButton";
        exitButton.TabIndex = 1;
        // cancelButton
        cancelButton.Location = new Point(218, 238);
        cancelButton.Size = new Size(160, 38);
        cancelButton.Text = "取消";
        cancelButton.Name = "cancelButton";
        cancelButton.TabIndex = 2;
        card.Controls.Add(title);
        card.Controls.Add(description);
        card.Controls.Add(releaseNotes);
        card.Controls.Add(minimizeButton);
        card.Controls.Add(exitButton);
        card.Controls.Add(cancelButton);
        // CloseConfirmationView
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiPalette.Window;
        Size = new Size(758, 459);
        Name = "CloseConfirmationView";
        Controls.Add(card);
        card.ResumeLayout(false);
        ResumeLayout(false);
    }
}
