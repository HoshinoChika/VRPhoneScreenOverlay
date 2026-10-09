#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class OnlinePageView
{
    private System.ComponentModel.IContainer components = null;
    private SurfacePanel placeholder = null!;
    private ModernIconView icon = null!;
    private Label title = null!;
    private Label subtitle = null!;

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
        placeholder = new SurfacePanel();
        icon = new ModernIconView();
        title = new Label();
        subtitle = new Label();
        SuspendLayout();
        placeholder.Bounds = new Rectangle(68, 92, 460, 240);
        placeholder.CornerRadius = 14;
        placeholder.Name = "placeholder";
        icon.BackColor = Color.Transparent;
        icon.Bounds = new Rectangle(198, 36, 64, 64);
        icon.Icon = UiIcons.Globe;
        icon.IconColor = UiPalette.Accent;
        title.AutoSize = false;
        title.BackColor = Color.Transparent;
        title.Bounds = new Rectangle(64, 112, 332, 36);
        title.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
        title.ForeColor = UiPalette.TextPrimary;
        title.Text = "联机功能暂未开放";
        title.TextAlign = ContentAlignment.MiddleCenter;
        subtitle.AutoSize = false;
        subtitle.BackColor = Color.Transparent;
        subtitle.Bounds = new Rectangle(54, 156, 352, 48);
        subtitle.Font = new Font("Microsoft YaHei UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point);
        subtitle.ForeColor = UiPalette.TextSecondary;
        subtitle.Text = "好友共享仍在准备中\r\n本地 USB 手机浮窗和控制功能不受影响";
        subtitle.TextAlign = ContentAlignment.MiddleCenter;
        placeholder.Controls.Add(icon);
        placeholder.Controls.Add(title);
        placeholder.Controls.Add(subtitle);
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = false;
        BackColor = UiPalette.Window;
        Controls.Add(placeholder);
        Name = "OnlinePageView";
        Size = new Size(596, 424);
        ResumeLayout(false);
    }
}
