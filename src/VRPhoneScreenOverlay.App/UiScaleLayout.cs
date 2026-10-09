using VRPhoneScreenOverlay.App.Views;

namespace VRPhoneScreenOverlay.App;

// Designer coordinates remain at 96 DPI. Always rescale from these original
// bounds, not the last monitor's rounded bounds. Pixel fonts prevent the OS DPI
// and our screen-size scaling from independently enlarging the same text twice.
internal sealed class UiScaleLayout : IDisposable
{
    private readonly MainShellView _root;
    private readonly List<Baseline> _controls = [];
    private Dictionary<FontSpec, Font> _fonts = [];
    private bool _disposed;

    public UiScaleLayout(MainShellView root)
    {
        _root = root;
        Capture(root, Point.Empty);
        root.Disposed += OnRootDisposed;
    }

    public void Apply(float scale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scale, 0);
        _root.ContentScale = scale;
        Dictionary<FontSpec, Font> replacement = [];
        foreach (Baseline item in _controls) { item.Control.SuspendLayout(); }
        try
        {
            foreach (Baseline item in _controls)
            {
                if (!replacement.TryGetValue(item.Font, out Font? font))
                {
                    font = new Font(item.Font.Name, item.Font.Pixels * scale,
                        item.Font.Style, GraphicsUnit.Pixel);
                    replacement.Add(item.Font, font);
                }
                item.Control.Font = font;
                item.Control.Padding = ScalePadding(item.Padding, scale);
                item.Control.Margin = ScalePadding(item.Margin, scale);
                Rectangle bounds = item.AbsoluteBounds;
                // Share the root coordinate grid, including the parent's rounding
                // phase, so adjacent edges and parent/child edges stay aligned.
                item.Control.Bounds = new Rectangle(
                    Round(bounds.X, scale) - Round(item.ParentOrigin.X, scale),
                    Round(bounds.Y, scale) - Round(item.ParentOrigin.Y, scale),
                    Round(bounds.Right, scale) - Round(bounds.Left, scale),
                    Round(bounds.Bottom, scale) - Round(bounds.Top, scale));
            }
        }
        finally
        {
            foreach (Baseline item in _controls)
            { if (item.Control is ContextHelpIcon help) { help.AlignToLabel(); } }
            _root.CloseConfirmation.ApplyModeLayout(scale);
            for (int index = _controls.Count - 1; index >= 0; index--)
            {
                _controls[index].Control.ResumeLayout(true);
                _controls[index].Control.Invalidate();
            }
            foreach (Font font in _fonts.Values) { font.Dispose(); }
            _fonts = replacement;
        }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _root.Disposed -= OnRootDisposed;
        foreach (Font font in _fonts.Values) { font.Dispose(); }
        _fonts.Clear();
    }

    private void OnRootDisposed(object? sender, EventArgs e) => Dispose();

    private void Capture(Control control, Point parentOrigin)
    {
        Font font = control.Font;
        Rectangle absolute = control.Bounds;
        absolute.Offset(parentOrigin);
        _controls.Add(new Baseline(control, absolute, parentOrigin, control.Padding, control.Margin,
            new FontSpec(font.Name, font.SizeInPoints * 96f / 72f, font.Style)));
        foreach (Control child in control.Controls) { Capture(child, absolute.Location); }
    }

    private static int Round(int value, float scale) => (int)MathF.Round(value * scale);

    private static Padding ScalePadding(Padding value, float scale) => new(
        Round(value.Left, scale), Round(value.Top, scale), Round(value.Right, scale), Round(value.Bottom, scale));

    private sealed record Baseline(Control Control, Rectangle AbsoluteBounds, Point ParentOrigin, Padding Padding, Padding Margin, FontSpec Font);
    private readonly record struct FontSpec(string Name, float Pixels, FontStyle Style);
}
