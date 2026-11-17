using System.Drawing.Drawing2D;

namespace VRPhoneScreenOverlay.App;

public sealed class SettingsScrollBar : Control
{
    private ScrollableControl? _target;
    private bool _hover;
    private int? _dragOffset;
    public SettingsScrollBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        BackColor = UiPalette.Window;
        Width = 22;
        TabStop = true;
        AccessibleName = "设置页面滚动条";
        AccessibleRole = AccessibleRole.ScrollBar;
    }
    internal void Bind(ScrollableControl target)
    {
        Unbind();
        _target = target;
        target.Scroll += TargetChanged;
        target.Layout += TargetLayout;
        target.MouseWheel += TargetWheel;
        Invalidate();
    }
    private void Unbind()
    {
        if (_target is null) { return; }
        _target.Scroll -= TargetChanged;
        _target.Layout -= TargetLayout;
        _target.MouseWheel -= TargetWheel;
        _target = null;
    }
    private void TargetChanged(object? sender, ScrollEventArgs e) => Invalidate();
    private void TargetLayout(object? sender, LayoutEventArgs e) => Invalidate();
    private void TargetWheel(object? sender, MouseEventArgs e) => Invalidate();
    internal int Maximum => _target is null ? 0 : Math.Max(0, _target.DisplayRectangle.Height - _target.ClientSize.Height);
    internal int Position => _target is null ? 0 : Math.Clamp(-_target.AutoScrollPosition.Y, 0, Maximum);
    internal Rectangle Thumb
    {
        get
        {
            int track = Math.Max(1, Height - 16);
            int viewport = _target?.ClientSize.Height ?? track;
            int length = Maximum == 0 ? track : Math.Clamp((int)((long)track * viewport / (viewport + Maximum)), Math.Min(32, track), track);
            int top = 8 + (Maximum == 0 ? 0 : (int)((long)(track - length) * Position / Maximum));
            return new((Width - 8) / 2, top, 8, length);
        }
    }
    internal void ScrollTo(int position)
    {
        if (_target is null) { return; }
        _target.AutoScrollPosition = new Point(0, Math.Clamp(position, 0, Maximum));
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Height < 20 || Maximum == 0) { return; }
        using var track = SurfacePanel.CreateRoundedRectangle(new Rectangle((Width - 4) / 2, 8, 4, Height - 16), 2);
        using var trackBrush = new SolidBrush(UiPalette.SurfaceRaised);
        e.Graphics.FillPath(trackBrush, track);
        using var thumb = SurfacePanel.CreateRoundedRectangle(Thumb, 4);
        using var brush = new SolidBrush(_dragOffset.HasValue ? UiPalette.Info : _hover || Focused ? UiPalette.TextSecondary : UiPalette.BorderSoft);
        e.Graphics.FillPath(brush, thumb);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Maximum == 0) { return; }
        Focus();
        if (e.Y >= Thumb.Top && e.Y < Thumb.Bottom) { _dragOffset = e.Y - Thumb.Top; Capture = true; }
        else { ScrollTo(Position + (e.Y < Thumb.Top ? -1 : 1) * (_target?.ClientSize.Height ?? 100)); }
        Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragOffset is int offset)
        {
            int travel = Height - 16 - Thumb.Height;
            if (travel > 0) { ScrollTo((int)Math.Clamp((long)(e.Y - 8 - offset) * Maximum / travel, 0, Maximum)); }
        }
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { _dragOffset = null; Invalidate(); } }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollTo(Position - e.Delta); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int page = _target?.ClientSize.Height ?? 100;
        int? next = e.KeyCode switch
        {
            Keys.Up => Position - 40,
            Keys.Down => Position + 40,
            Keys.PageUp => Position - page,
            Keys.PageDown => Position + page,
            Keys.Home => 0,
            Keys.End => Maximum,
            _ => null
        };
        if (next is int value) { ScrollTo(value); e.Handled = true; }
    }
    protected override void Dispose(bool disposing) { if (disposing) { Unbind(); } base.Dispose(disposing); }
}
