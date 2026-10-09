using System.ComponentModel;
using System.Drawing.Drawing2D;
using VRPhoneScreenOverlay.App.Views;

namespace VRPhoneScreenOverlay.App;

/// <summary>Focusable vector help icon. The explanation is rendered inside the same window.</summary>
public sealed class ContextHelpIcon : Control
{
    private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 300 };
    private string _helpText = string.Empty;
    private Label? _targetLabel;
    private bool _aligning;
    private MainShellView? _host;
    private bool _hovered;
    private readonly List<Control> _ancestors = [];

    public ContextHelpIcon()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(22, 22);
        Cursor = Cursors.Help;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "查看说明";
        _hoverTimer.Tick += OnHoverTick;
    }

    [DefaultValue(""), Localizable(true)]
    public string HelpText
    {
        get => _helpText;
        set { _helpText = value ?? string.Empty; AccessibleDescription = _helpText; }
    }

    [DefaultValue(null)]
    public Label? TargetLabel
    {
        get => _targetLabel;
        set
        {
            if (ReferenceEquals(value, _targetLabel)) { return; }
            if (_targetLabel is not null)
            {
                _targetLabel.TextChanged -= OnLabelChanged;
                _targetLabel.FontChanged -= OnLabelChanged;
                _targetLabel.LocationChanged -= OnLabelChanged;
                _targetLabel.SizeChanged -= OnLabelChanged;
                _targetLabel.ParentChanged -= OnLabelChanged;
            }
            _targetLabel = value;
            if (_targetLabel is not null)
            {
                _targetLabel.TextChanged += OnLabelChanged;
                _targetLabel.FontChanged += OnLabelChanged;
                _targetLabel.LocationChanged += OnLabelChanged;
                _targetLabel.SizeChanged += OnLabelChanged;
                _targetLabel.ParentChanged += OnLabelChanged;
            }
            AlignToLabel();
        }
    }

    private void OnLabelChanged(object? sender, EventArgs e) => AlignToLabel();
    internal void AlignToLabel()
    {
        if (_aligning || _targetLabel is null || Parent is null || _targetLabel.Parent != Parent) { return; }
        _aligning = true;
        try
        {
            int width = TextRenderer.MeasureText(_targetLabel.Text, _targetLabel.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
            _targetLabel.TextAlign = ContentAlignment.MiddleLeft;
            _targetLabel.Width = width;
            int gap = Math.Max(2, Width / 11);
            Location = new Point(_targetLabel.Right + gap, _targetLabel.Top + (_targetLabel.Height - Height) / 2);
        }
        finally { _aligning = false; }
    }

    protected override void OnMouseEnter(EventArgs e)
    { base.OnMouseEnter(e); _hovered = true; _hoverTimer.Start(); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e)
    { base.OnMouseLeave(e); _hovered = false; _hoverTimer.Stop(); HideHelp(); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (!_hovered) { HideHelp(); } Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); ShowHelp(); }
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Enter or Keys.Space or Keys.Escape or Keys.F1 || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { HideHelp(); e.Handled = true; e.SuppressKeyPress = true; }
        else if (e.KeyCode is Keys.Enter or Keys.Space or Keys.F1) { ShowHelp(); e.Handled = true; e.SuppressKeyPress = true; }
    }
    protected override void OnVisibleChanged(EventArgs e)
    { base.OnVisibleChanged(e); if (!Visible) { _hoverTimer.Stop(); HideHelp(); } }
    protected override void OnParentChanged(EventArgs e)
    { HideHelp(); base.OnParentChanged(e); AlignToLabel(); }

    private void OnHoverTick(object? sender, EventArgs e) { _hoverTimer.Stop(); ShowHelp(); }
    internal void ShowHelp()
    {
        if (!Visible || !Enabled || string.IsNullOrWhiteSpace(HelpText)) { return; }
        HideHelp();
        Control? parent = Parent;
        while (parent is not null)
        {
            _ancestors.Add(parent);
            parent.VisibleChanged += OnAncestorChanged;
            parent.ParentChanged += OnAncestorChanged;
            if (parent is MainShellView shell) { _host = shell; break; }
            parent = parent.Parent;
        }
        _host?.ShowContextHelp(this, HelpText);
    }
    private void OnAncestorChanged(object? sender, EventArgs e) => HideHelp();
    internal void HideHelp()
    {
        foreach (Control ancestor in _ancestors)
        {
            ancestor.VisibleChanged -= OnAncestorChanged;
            ancestor.ParentChanged -= OnAncestorChanged;
        }
        _ancestors.Clear();
        _host?.HideContextHelp(this);
        _host = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float size = Math.Min(Width, Height);
        float left = (Width - size) / 2f;
        float top = (Height - size) / 2f;
        Color color = _hovered || Focused ? UiPalette.TextPrimary : UiPalette.TextSecondary;
        using Pen pen = new(color, Math.Max(1.25f, size / 14f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using SolidBrush brush = new(color);
        e.Graphics.DrawEllipse(pen, left + size * .15f, top + size * .15f, size * .7f, size * .7f);
        e.Graphics.DrawLine(pen, left + size * .5f, top + size * .32f, left + size * .5f, top + size * .52f);
        e.Graphics.FillEllipse(brush, left + size * .455f, top + size * .63f, size * .09f, size * .09f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _hoverTimer.Stop(); _hoverTimer.Tick -= OnHoverTick; _hoverTimer.Dispose(); HideHelp(); TargetLabel = null; }
        base.Dispose(disposing);
    }
}

/// <summary>Root-owned help card, bounded to the client area; never creates a desktop popup window.</summary>
public sealed class ContextHelpPopup : Control
{
    private string _message = string.Empty;
    internal Control? Owner { get; private set; }
    public ContextHelpPopup()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        Visible = false;
        TabStop = false;
        BackColor = Color.Transparent;
        ForeColor = UiPalette.TextPrimary;
        AccessibleRole = AccessibleRole.ToolTip;
    }

    internal void ShowFor(Control owner, string message, float scale)
    {
        if (Parent is null) { return; }
        Owner = owner;
        _message = message;
        AccessibleName = "功能说明";
        AccessibleDescription = message;
        Font = owner.Font;
        int padding = Math.Max(10, (int)(14 * scale));
        int naturalWidth = TextRenderer.MeasureText(message, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        int width = Math.Min(Math.Min((int)(430 * scale), naturalWidth + padding * 2), Parent.ClientSize.Width - padding * 2);
        if (width <= padding * 2) { HideHelp(); return; }
        Size text = TextRenderer.MeasureText(message, Font, new Size(width - padding * 2, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        int height = Math.Min(text.Height + padding * 2, Parent.ClientSize.Height - padding * 2);
        Rectangle anchor = Parent.RectangleToClient(owner.RectangleToScreen(owner.ClientRectangle));
        int x = Math.Clamp(anchor.Right + padding / 2, padding, Parent.ClientSize.Width - width - padding);
        int y = anchor.Bottom + padding / 2;
        if (y + height > Parent.ClientSize.Height - padding) { y = anchor.Top - height - padding / 2; }
        y = Math.Clamp(y, padding, Math.Max(padding, Parent.ClientSize.Height - height - padding));
        Padding = new Padding(padding);
        Bounds = new Rectangle(x, y, width, height);
        Visible = true;
        BringToFront();
        Invalidate();
    }

    internal void HideHelp() { Visible = false; Owner = null; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using Pen border = new(UiPalette.HelpBorder, Math.Max(1.5f, Padding.Left / 9f));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = SurfacePanel.CreateRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), Math.Max(6, Padding.Left / 2));
        using SolidBrush background = new(UiPalette.HelpSurface);
        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, _message, Font,
            new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical),
            ForeColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
