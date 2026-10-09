namespace VRPhoneScreenOverlay.App;

internal interface IWindowTray : IDisposable
{
    public bool Visible { get; set; }
    public event EventHandler? RestoreRequested;
    public event EventHandler? ExitRequested;
}

internal sealed class WindowTray : IWindowTray
{
    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;

    public WindowTray(Icon icon, string text)
    {
        _menu.Items.Add("还原窗口", null, (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty));
        _menu.Items.Add("退出程序", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _icon = new NotifyIcon { Icon = icon, Text = text, ContextMenuStrip = _menu };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) { RestoreRequested?.Invoke(this, EventArgs.Empty); }
        };
        _icon.DoubleClick += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    public bool Visible { get => _icon.Visible; set => _icon.Visible = value; }
    public event EventHandler? RestoreRequested;
    public event EventHandler? ExitRequested;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
