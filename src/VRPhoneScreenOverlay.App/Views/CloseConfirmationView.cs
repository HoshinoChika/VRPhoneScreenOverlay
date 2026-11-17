namespace VRPhoneScreenOverlay.App.Views;

public partial class CloseConfirmationView : UserControl
{
    public CloseConfirmationView() { InitializeComponent(); }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { if (IsUpdate) { exitButton.PerformClick(); } else { cancelButton.PerformClick(); } return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    internal bool IsUpdate { get; private set; }
    internal string UpdateNotes => releaseNotes.Text;
    internal void ConfigureUpdate(string version, string notes)
    {
        IsUpdate = true;
        card.Location = new Point(80, 28);
        card.Size = new Size(596, 398);
        title.Text = "发现新版本";
        title.Location = new Point(28, 20);
        description.Location = new Point(28, 68);
        description.Text = "版本 " + version;
        description.ForeColor = UiPalette.TextSecondary;
        releaseNotes.Text = string.IsNullOrWhiteSpace(notes) ? "此版本暂无更新说明。" :
            notes[..Math.Min(notes.Length, 12000)].ReplaceLineEndings("\r\n");
        releaseNotes.Visible = true;
        minimizeButton.Location = new Point(28, 280);
        exitButton.Location = new Point(308, 280);
        minimizeButton.Text = "更新";
        exitButton.Text = "暂不更新";
        exitButton.Tone = UiButtonTone.Neutral;
        cancelButton.Visible = false;
        ApplyModeLayout(Width / 758f);
    }

    internal void ConfigureClose()
    {
        IsUpdate = false;
        card.Location = new Point(80, 78);
        card.Size = new Size(596, 304);
        title.Text = "关闭窗口";
        title.Location = new Point(28, 28);
        description.Location = new Point(28, 86);
        description.Text = "最小化保留浮窗，退出关闭浮窗。";
        description.ForeColor = UiPalette.TextSecondary;
        releaseNotes.Visible = false;
        minimizeButton.Location = new Point(28, 142);
        exitButton.Location = new Point(308, 142);
        minimizeButton.Text = "最小化到托盘";
        exitButton.Text = "退出程序";
        exitButton.Tone = UiButtonTone.Danger;
        cancelButton.Visible = true;
        ApplyModeLayout(Width / 758f);
    }

    internal void ConfigureExiting(bool failed = false)
    {
        ConfigureClose();
        title.Text = "正在退出";
        description.Text = failed ? "保存失败，正在等待结束本软件（最长约 20 秒）。" : "正在保存并退出；超时约 20 秒后自动结束本软件。";
        minimizeButton.Enabled = false;
        exitButton.Enabled = false;
        cancelButton.Enabled = false;
    }

    internal void ApplyModeLayout(float scale)
    {
        Rectangle Scaled(int x, int y, int width, int height) => new(
            (int)MathF.Round(x * scale), (int)MathF.Round(y * scale),
            (int)MathF.Round(width * scale), (int)MathF.Round(height * scale));
        card.Bounds = IsUpdate ? Scaled(24, 12, 710, 435) : Scaled(80, 78, 596, 304);
        title.Bounds = IsUpdate ? Scaled(24, 14, 662, 38) : Scaled(28, 28, 540, 42);
        description.Bounds = IsUpdate ? Scaled(24, 56, 662, 30) : Scaled(28, 86, 540, 34);
        releaseNotes.Bounds = Scaled(24, 92, 662, 240);
        minimizeButton.Bounds = IsUpdate ? Scaled(24, 350, 318, 56) : Scaled(28, 142, 260, 62);
        exitButton.Bounds = IsUpdate ? Scaled(368, 350, 318, 56) : Scaled(308, 142, 260, 62);
        cancelButton.Bounds = Scaled(218, 238, 160, 38);
    }

    internal Label Description => description;
    internal ModernButton MinimizeButton => minimizeButton;
    internal ModernButton ExitButton => exitButton;
    internal ModernButton CancelButton => cancelButton;
}
