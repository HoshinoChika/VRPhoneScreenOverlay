using System.Drawing.Drawing2D;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

public sealed class SteamVrBindingNotice : Panel
{
    private readonly Label _title;
    private readonly Label _message;
    private readonly ModernButton _reloadButton;
    private Color _fillColor = UiPalette.Sidebar;

    public SteamVrBindingNotice()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        Size = new Size(596, 78);
        BackColor = Color.Transparent;
        ForeColor = UiPalette.TextSecondary;
        _title = new Label
        {
            AutoSize = false,
            Location = new Point(12, 5),
            Size = new Size(570, 18),
            BackColor = Color.Transparent,
            Font = new Font(Font, FontStyle.Bold),
            Text = "状态",
            UseMnemonic = false,
        };
        _message = new Label
        {
            AutoSize = false,
            Location = new Point(12, 27),
            Size = new Size(350, 44),
            BackColor = Color.Transparent,
            Text = "绑定正常",
            UseMnemonic = false,
        };
        _reloadButton = new ModernButton
        {
            AutoSize = false,
            Location = new Point(370, 36),
            Size = new Size(210, 30),
            Icon = UiIcons.Undo,
            Text = "重新加载本地绑定",
            Margin = new Padding(0),
            Visible = false,
        };
        _reloadButton.Click += OnReloadClicked;
        Controls.Add(_title);
        Controls.Add(_message);
        Controls.Add(_reloadButton);
    }

    public event EventHandler? ReloadRequested;

    public void UpdateNotice(
        OpenVrBindingHealthState state,
        bool showNotice,
        string message)
        => PresentNotice(state, showNotice, message, connectionIssue: false);

    internal void UpdateConnectionNotice(string message)
        => PresentNotice(OpenVrBindingHealthState.Failed, true, message, connectionIssue: true);

    private void PresentNotice(OpenVrBindingHealthState state, bool showNotice, string message, bool connectionIssue)
    {
        bool visibleNotice = showNotice &&
            state is OpenVrBindingHealthState.Loading or OpenVrBindingHealthState.Failed;
        Visible = visibleNotice;
        if (!visibleNotice)
        {
            ApplyColors(UiPalette.Sidebar, UiPalette.TextSecondary);
            SetControlText(_title, "状态");
            SetControlText(
                _message,
                "绑定正常");
            _reloadButton.Visible = false;
            return;
        }

        bool failed = state == OpenVrBindingHealthState.Failed;
        ApplyColors(
            failed ? UiPalette.DangerSurface : UiPalette.SurfaceRaised,
            failed ? UiPalette.Danger : UiPalette.TextPrimary);
        SetControlText(
            _title,
            connectionIssue ? "SteamVR连接超时" : failed ? "SteamVR 手柄绑定加载失败" : "正在加载 SteamVR 手柄绑定…");
        SetControlText(
            _message,
            failed && !connectionIssue
                ? "绑定加载失败。请先关闭桌面的 SteamVR 绑定页面，再重新加载本地绑定。"
                : message);
        _reloadButton.Visible = failed && !connectionIssue;
    }

    private static void SetControlColor(Control control, Color backColor, Color foreColor)
    {
        if (control.BackColor != backColor)
        {
            control.BackColor = backColor;
        }

        if (control.ForeColor != foreColor)
        {
            control.ForeColor = foreColor;
        }
    }

    private void ApplyColors(Color fillColor, Color foreColor)
    {
        if (_fillColor != fillColor)
        {
            _fillColor = fillColor;
            Invalidate();
        }

        if (ForeColor != foreColor)
        {
            ForeColor = foreColor;
        }

        SetControlColor(_title, Color.Transparent, foreColor);
        SetControlColor(_message, Color.Transparent, foreColor);
    }

    private static void SetControlText(Control control, string text)
    {
        if (!string.Equals(control.Text, text, StringComparison.Ordinal))
        {
            control.Text = text;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (ClientSize.Width < 2 || ClientSize.Height < 2)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        using GraphicsPath path = SurfacePanel.CreateRoundedRectangle(bounds, 10);
        using SolidBrush background = new(_fillColor);
        using Pen border = new(UiPalette.BorderSoft);
        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reloadButton.Click -= OnReloadClicked;
        }

        base.Dispose(disposing);
    }

    private void OnReloadClicked(object? sender, EventArgs eventArgs) =>
        ReloadRequested?.Invoke(this, EventArgs.Empty);
}
