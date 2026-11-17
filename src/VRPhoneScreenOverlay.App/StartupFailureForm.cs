namespace VRPhoneScreenOverlay.App;

internal sealed partial class StartupFailureForm : Form
{
    internal StartupFailureForm(bool reportSaved)
    {
        InitializeComponent();
        details.Text = "软件未能完成启动或运行，已停止本次运行。\r\n\r\n" +
            (reportSaved ? "已保存启动故障记录。请通过 QQ 联系作者协助排查。" : "故障记录未能保存。请截图并通过 QQ 联系作者协助排查。") +
            "\r\n\r\n错误代码：APP_STARTUP_FAILED";
    }
    private void CloseButton_Click(object? sender, EventArgs e) => Close();
}
