using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Drawing2D;
using System.Globalization;
using VRPhoneScreenOverlay.Presentation;

namespace VRPhoneScreenOverlay.App;

internal static class UiPalette
{
    public static Color Window { get; } = Color.FromArgb(unchecked((int)UiTheme.Window));

    public static Color Header { get; } = Color.FromArgb(unchecked((int)UiTheme.Header));

    public static Color Sidebar { get; } = Color.FromArgb(unchecked((int)UiTheme.Sidebar));

    public static Color Surface { get; } = Color.FromArgb(unchecked((int)UiTheme.Surface));

    public static Color SurfaceRaised { get; } = Color.FromArgb(unchecked((int)UiTheme.SurfaceRaised));

    public static Color SurfaceHover { get; } = Color.FromArgb(unchecked((int)UiTheme.SurfaceHover));

    public static Color Border { get; } = Color.FromArgb(unchecked((int)UiTheme.Border));

    public static Color BorderSoft { get; } = Color.FromArgb(unchecked((int)UiTheme.BorderSoft));

    public static Color WindowBorder { get; } = Color.FromArgb(unchecked((int)UiTheme.WindowBorder));

    public static Color HelpSurface { get; } = Color.FromArgb(46, 66, 92);

    public static Color HelpBorder { get; } = Color.FromArgb(139, 176, 221);

    public static Color TextPrimary { get; } = Color.FromArgb(unchecked((int)UiTheme.TextPrimary));

    public static Color TextSecondary { get; } = Color.FromArgb(unchecked((int)UiTheme.TextSecondary));

    public static Color TextMuted { get; } = Color.FromArgb(unchecked((int)UiTheme.TextMuted));

    public static Color Accent { get; } = Color.FromArgb(unchecked((int)UiTheme.Accent));

    public static Color AccentHover { get; } = Color.FromArgb(unchecked((int)UiTheme.AccentHover));

    public static Color AccentDark { get; } = Color.FromArgb(unchecked((int)UiTheme.AccentDark));

    public static Color Success { get; } = Color.FromArgb(unchecked((int)UiTheme.Success));

    public static Color SuccessButton { get; } = Color.FromArgb(unchecked((int)UiTheme.SuccessButton));

    public static Color SuccessButtonHover { get; } = Color.FromArgb(unchecked((int)UiTheme.SuccessButtonHover));

    public static Color SuccessButtonPressed { get; } = Color.FromArgb(unchecked((int)UiTheme.SuccessButtonPressed));

    public static Color Warning { get; } = Color.FromArgb(unchecked((int)UiTheme.Warning));

    public static Color Danger { get; } = Color.FromArgb(unchecked((int)UiTheme.Danger));

    public static Color DangerHover { get; } = Color.FromArgb(unchecked((int)UiTheme.DangerHover));

    public static Color DangerPressed { get; } = Color.FromArgb(unchecked((int)UiTheme.DangerPressed));

    public static Color DangerButton { get; } = Color.FromArgb(unchecked((int)UiTheme.DangerButton));

    public static Color DangerButtonHover { get; } = Color.FromArgb(unchecked((int)UiTheme.DangerButtonHover));

    public static Color DangerButtonPressed { get; } = Color.FromArgb(unchecked((int)UiTheme.DangerButtonPressed));

    /// <summary>Soft translucent accent used for hover glows.</summary>
    public static Color AccentGlow { get; } = Color.FromArgb(unchecked((int)UiTheme.AccentGlow));

    /// <summary>Translucent white used for hover glows on secondary controls.</summary>
    public static Color LightGlow { get; } = Color.FromArgb(unchecked((int)UiTheme.LightGlow));

    /// <summary>Background of the selected sidebar navigation pill.</summary>
    public static Color NavigationSelected { get; } = Color.FromArgb(unchecked((int)UiTheme.NavigationSelected));

    /// <summary>Text colour for an operation that is still in progress.</summary>
    public static Color Info { get; } = Color.FromArgb(unchecked((int)UiTheme.Info));

    /// <summary>Background of a toggle that is switched on.</summary>
    public static Color ToggleOn { get; } = Color.FromArgb(unchecked((int)UiTheme.ToggleOn));

    /// <summary>Background of a toggle that is switched off.</summary>
    public static Color ToggleOff { get; } = Color.FromArgb(unchecked((int)UiTheme.ToggleOff));

    /// <summary>Checked toggle switch, resting.</summary>
    public static Color SwitchOn { get; } = Color.FromArgb(unchecked((int)UiTheme.SwitchOn));

    /// <summary>Checked toggle switch, hovered.</summary>
    public static Color SwitchOnHover { get; } = Color.FromArgb(unchecked((int)UiTheme.SwitchOnHover));

    /// <summary>Checked toggle switch, pressed.</summary>
    public static Color SwitchOnPressed { get; } = Color.FromArgb(unchecked((int)UiTheme.SwitchOnPressed));

    /// <summary>Panel background for a warning notice.</summary>
    public static Color DangerSurface { get; } = Color.FromArgb(unchecked((int)UiTheme.DangerSurface));

    /// <summary>Panel border for a warning notice.</summary>
    public static Color DangerBorder { get; } = Color.FromArgb(unchecked((int)UiTheme.DangerBorder));

    /// <summary>Background of the legacy flat buttons on the home page.</summary>
    public static Color LegacyButton { get; } = Color.FromArgb(unchecked((int)UiTheme.LegacyButton));

    /// <summary>Background of the legacy device list.</summary>
    public static Color LegacyListBackground { get; } = Color.FromArgb(unchecked((int)UiTheme.LegacyListBackground));
}

public enum UiButtonTone
{
    Neutral,
    Success,
    Danger,
}

public sealed class SurfacePanel : Panel
{
    private int _cornerRadius = 12;
    private Color _surfaceColor = UiPalette.Surface;
    private Color _borderColor = UiPalette.BorderSoft;

    public SurfacePanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
    }

    [DefaultValue(12)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(0, value);
            Invalidate();
        }
    }

    public Color SurfaceColor
    {
        get => _surfaceColor;
        set
        {
            _surfaceColor = value;
            Invalidate();
        }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    private void ResetSurfaceColor() => SurfaceColor = UiPalette.Surface;

    private bool ShouldSerializeSurfaceColor() => SurfaceColor != UiPalette.Surface;

    private void ResetBorderColor() => BorderColor = UiPalette.BorderSoft;

    private bool ShouldSerializeBorderColor() => BorderColor != UiPalette.BorderSoft;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (ClientSize.Width < 2 || ClientSize.Height < 2)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        using GraphicsPath path = CreateRoundedRectangle(bounds, _cornerRadius);
        using SolidBrush background = new(_surfaceColor);
        using Pen border = new(_borderColor);
        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(border, path);
    }

    internal static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        GraphicsPath path = new();
        int diameter = Math.Min(Math.Min(radius * 2, bounds.Width), bounds.Height);
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        Rectangle arc = new(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public sealed class ModernIconView : Control
{
    private UiIcon _icon;
    private Color _iconColor = UiPalette.Accent;
    private Bitmap? _bundledIcon;

    public ModernIconView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    [DefaultValue(UiIcon.None)]
    public UiIcon Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            Invalidate();
        }
    }

    public Color IconColor
    {
        get => _iconColor;
        set
        {
            _iconColor = value;
            Invalidate();
        }
    }

    private void ResetIconColor() => IconColor = UiPalette.Accent;

    private bool ShouldSerializeIconColor() => IconColor != UiPalette.Accent;

    internal void SetBundledApplicationIcon(BundledApplicationIcon icon)
    {
        _bundledIcon?.Dispose();
        _bundledIcon = BundledApplicationIcons.Load(icon);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_bundledIcon is not null)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(_bundledIcon, ClientRectangle);
            return;
        }

        UiIcons.Draw(e.Graphics, _icon, ClientRectangle, _iconColor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bundledIcon?.Dispose();
            _bundledIcon = null;
        }

        base.Dispose(disposing);
    }
}

public sealed class ApplicationIconView : Control
{
    private readonly Bitmap? _applicationIcon;

    public ApplicationIconView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        TabStop = false;
        if (LicenseManager.UsageMode != LicenseUsageMode.Designtime)
        {
            using Icon? icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            _applicationIcon = icon?.ToBitmap();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_applicationIcon is null)
        {
            return;
        }

        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.DrawImage(_applicationIcon, ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _applicationIcon?.Dispose();
        }

        base.Dispose(disposing);
    }
}

public sealed class WindowControlButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public WindowControlButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = false;
        UseVisualStyleBackColor = false;
    }

    [DefaultValue(UiIcon.None)]
    public UiIcon Icon { get; set; }

    [DefaultValue(false)]
    public bool DangerOnHover { get; set; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        Graphics graphics = pevent.Graphics;
        graphics.Clear(UiPalette.Header);
        if (_hovered)
        {
            using SolidBrush hover = new(
                DangerOnHover
                    ? _pressed ? UiPalette.DangerPressed : UiPalette.DangerHover
                    : _pressed ? UiPalette.Surface : UiPalette.SurfaceHover);
            graphics.FillRectangle(hover, ClientRectangle);
        }

        int iconSize = UiLayoutMetrics.Pixels(this, 18);
        Rectangle iconBounds = new(
            (Width - iconSize) / 2,
            (Height - iconSize) / 2,
            iconSize,
            iconSize);
        UiIcons.Draw(graphics, Icon, iconBounds, UiPalette.TextSecondary, 1.8F);
    }
}

public sealed class ModernButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private UiButtonTone _tone;
    private bool _verticalContent;
    private Bitmap? _bundledIcon;

    public ModernButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        BackColor = UiPalette.Surface;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = new Font(UiTheme.FontFamily, 10.5F, FontStyle.Regular, GraphicsUnit.Point);
        ForeColor = UiPalette.TextPrimary;
        UseVisualStyleBackColor = false;
    }

    [DefaultValue(false)]
    public bool Primary { get; set; }

    [DefaultValue(UiButtonTone.Neutral)]
    public UiButtonTone Tone
    {
        get => _tone;
        set
        {
            _tone = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool VerticalContent
    {
        get => _verticalContent;
        set
        {
            _verticalContent = value;
            Invalidate();
        }
    }

    internal bool HasApplicationIcon => _bundledIcon is not null;

    /// <summary>Optional application vector icon drawn before the button text.</summary>
    [DefaultValue(UiIcon.None)]
    public UiIcon Icon { get; set; }

    internal void SetBundledApplicationIcon(BundledApplicationIcon icon)
    {
        _bundledIcon?.Dispose();
        _bundledIcon = BundledApplicationIcons.Load(icon);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (ClientSize.Width < 2 || ClientSize.Height < 2)
        {
            return;
        }

        Graphics graphics = pevent.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Parent is SurfacePanel panel ? panel.SurfaceColor : BackColor);
        Rectangle bounds = new(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        if (Enabled && _hovered)
        {
            using GraphicsPath glowPath = SurfacePanel.CreateRoundedRectangle(bounds, 12);
            using Pen glow = new(
                Primary || Tone != UiButtonTone.Neutral
                    ? UiPalette.AccentGlow
                    : UiPalette.LightGlow,
                3F);
            graphics.DrawPath(glow, glowPath);
        }

        Rectangle body = Rectangle.Inflate(bounds, -2, -2);
        using GraphicsPath bodyPath = SurfacePanel.CreateRoundedRectangle(body, 10);
        if ((Primary || Tone != UiButtonTone.Neutral) && Enabled && !_pressed)
        {
            using LinearGradientBrush gradient = new(
                body,
                GetProminentHoverColor(),
                GetProminentColor(),
                LinearGradientMode.Vertical);
            graphics.FillPath(gradient, bodyPath);
        }
        else
        {
            using SolidBrush fill = new(GetFillColor());
            graphics.FillPath(fill, bodyPath);
        }

        using Pen border = new(GetBorderColor());
        graphics.DrawPath(border, bodyPath);
        DrawContent(graphics, bounds);
    }

    private void DrawContent(Graphics graphics, Rectangle bounds)
    {
        if (Icon == UiIcon.None && _bundledIcon is null)
        {
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                Rectangle.Inflate(bounds, -3, 0),
                Enabled ? ForeColor : UiPalette.TextMuted,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            return;
        }

        if (VerticalContent)
        {
            int verticalIconSize = UiLayoutMetrics.Pixels(this, 22);
            Rectangle verticalIconBounds = new(
                (ClientSize.Width - verticalIconSize) / 2,
                Math.Max(UiLayoutMetrics.Pixels(this, 9), (ClientSize.Height / 3) - (verticalIconSize / 2)),
                verticalIconSize,
                verticalIconSize);
            DrawIcon(graphics, verticalIconBounds);
            Rectangle verticalTextBounds = new(
                UiLayoutMetrics.Pixels(this, 5),
                ClientSize.Height / 2,
                Math.Max(1, ClientSize.Width - UiLayoutMetrics.Pixels(this, 10)),
                Math.Max(1, (ClientSize.Height / 2) - UiLayoutMetrics.Pixels(this, 5)));
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                verticalTextBounds,
                Enabled ? ForeColor : UiPalette.TextMuted,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.WordBreak |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            return;
        }

        int iconSize = UiLayoutMetrics.Pixels(this, 19);
        Size textSize = TextRenderer.MeasureText(
            graphics,
            Text,
            Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        int gap = UiLayoutMetrics.Pixels(this, 6);
        int contentWidth = iconSize + gap + textSize.Width;
        int left = Math.Max(2, (ClientSize.Width - contentWidth) / 2);
        Rectangle iconBounds = new(
            left,
            (ClientSize.Height - iconSize) / 2,
            iconSize,
            iconSize);
        DrawIcon(graphics, iconBounds);
        Rectangle labelBounds = new(
            left + iconSize + gap,
            0,
            Math.Max(0, ClientSize.Width - left - iconSize - gap),
            ClientSize.Height);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            labelBounds,
            Enabled ? ForeColor : UiPalette.TextMuted,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bundledIcon?.Dispose();
            _bundledIcon = null;
        }

        base.Dispose(disposing);
    }

    private void DrawIcon(Graphics graphics, Rectangle bounds)
    {
        if (_bundledIcon is not null)
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(_bundledIcon, bounds);
            return;
        }

        UiIcons.Draw(
            graphics,
            Icon,
            bounds,
            Enabled ? ForeColor : UiPalette.TextMuted);
    }

    private Color GetFillColor()
    {
        if (!Enabled)
        {
            return UiPalette.Surface;
        }

        if (Primary || Tone != UiButtonTone.Neutral)
        {
            return _pressed ? GetProminentPressedColor() : GetProminentColor();
        }

        return _pressed
            ? UiPalette.Sidebar
            : _hovered
                ? UiPalette.SurfaceHover
                : UiPalette.SurfaceRaised;
    }

    private Color GetBorderColor()
    {
        if (!Enabled)
        {
            return UiPalette.BorderSoft;
        }

        return Primary || Tone != UiButtonTone.Neutral
            ? GetProminentColor()
            : UiPalette.Border;
    }

    private Color GetProminentColor() => Tone switch
    {
        UiButtonTone.Success => UiPalette.SuccessButton,
        UiButtonTone.Danger => UiPalette.DangerButton,
        _ => UiPalette.Accent,
    };

    private Color GetProminentHoverColor() => Tone switch
    {
        UiButtonTone.Success => UiPalette.SuccessButtonHover,
        UiButtonTone.Danger => UiPalette.DangerButtonHover,
        _ => UiPalette.AccentHover,
    };

    private Color GetProminentPressedColor() => Tone switch
    {
        UiButtonTone.Success => UiPalette.SuccessButtonPressed,
        UiButtonTone.Danger => UiPalette.DangerButtonPressed,
        _ => UiPalette.AccentDark,
    };
}

public sealed class NavigationButton : Button
{
    private bool _hovered;
    private bool _selected;

    public NavigationButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = new Font(UiTheme.FontFamily, 10.5F, FontStyle.Regular, GraphicsUnit.Point);
        ForeColor = UiPalette.TextSecondary;
        TextAlign = ContentAlignment.MiddleLeft;
        UseVisualStyleBackColor = false;
    }

    /// <summary>Optional application vector icon drawn at the start of the pill.</summary>
    [DefaultValue(UiIcon.None)]
    public UiIcon Icon { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (ClientSize.Width < 2 || ClientSize.Height < 2)
        {
            return;
        }

        Graphics graphics = pevent.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(UiPalette.Sidebar);
        Rectangle pill = new(UiLayoutMetrics.Pixels(this, 5), UiLayoutMetrics.Pixels(this, 3),
            ClientSize.Width - UiLayoutMetrics.Pixels(this, 11), ClientSize.Height - UiLayoutMetrics.Pixels(this, 7));
        using GraphicsPath path = SurfacePanel.CreateRoundedRectangle(pill, 12);
        using SolidBrush fill = new(
            _selected
                ? UiPalette.NavigationSelected
                : _hovered
                    ? UiPalette.SurfaceHover
                    : UiPalette.Sidebar);
        graphics.FillPath(fill, path);
        if (_selected)
        {
            using SolidBrush accent = new(UiPalette.Accent);
            graphics.FillRectangle(accent, UiLayoutMetrics.Pixels(this, 11), pill.Y + UiLayoutMetrics.Pixels(this, 7),
                UiLayoutMetrics.Pixels(this, 3), Math.Max(1, pill.Height - UiLayoutMetrics.Pixels(this, 14)));
        }

        int iconSize = UiLayoutMetrics.Pixels(this, 20);
        Rectangle iconBounds = new(UiLayoutMetrics.Pixels(this, 16), (Height - iconSize) / 2, iconSize, iconSize);
        if (Icon != UiIcon.None)
        {
            UiIcons.Draw(
                graphics,
                Icon,
                iconBounds,
                _selected ? UiPalette.Accent : UiPalette.TextSecondary);
        }

        Rectangle textBounds = new(UiLayoutMetrics.Pixels(this, 42), 0, Math.Max(0, Width - UiLayoutMetrics.Pixels(this, 49)), Height);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            textBounds,
            _selected ? UiPalette.TextPrimary : UiPalette.TextSecondary,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix);
    }
}

public sealed class ModernDateTimeInput : UserControl
{
    private readonly TextBox _editor;
    private DateTime _maximum = DateTime.MaxValue.Date;
    private DateTime _minimum = new(1900, 1, 1);
    private DateTime _value = DateTime.Now;
    private bool _updatingText;

    public ModernDateTimeInput()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        BackColor = Color.Transparent;
        ForeColor = UiPalette.TextPrimary;
        Size = new Size(200, 34);
        _editor = new TextBox
        {
            BackColor = UiPalette.SurfaceRaised,
            BorderStyle = BorderStyle.None,
            ForeColor = UiPalette.TextPrimary,
            MaxLength = UiInputPolicy.DateTimeFormat.Length,
            ShortcutsEnabled = true,
            TextAlign = HorizontalAlignment.Left,
        };
        _editor.Enter += OnEditorEnter;
        _editor.Leave += OnEditorLeave;
        _editor.KeyDown += OnEditorKeyDown;
        _editor.KeyPress += OnEditorKeyPress;
        Controls.Add(_editor);
        LayoutEditor();
        UpdateText();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime MinDate
    {
        get => _minimum;
        set
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _maximum);
            _minimum = value;
            Value = _value;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime MaxDate
    {
        get => _maximum;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, _minimum);
            _maximum = value;
            Value = _value;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime Value
    {
        get => _value;
        set
        {
            _value = value < _minimum
                ? _minimum
                : value > _maximum
                    ? _maximum
                    : value;
            UpdateText();
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_editor is not null)
        {
            _editor.Font = Font;
            LayoutEditor();
        }
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        if (_editor is not null)
        {
            _editor.ForeColor = ForeColor;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutEditor();
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        _editor.Focus();
        _editor.SelectAll();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Rectangle bounds = new(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using GraphicsPath path = SurfacePanel.CreateRoundedRectangle(bounds, 10);
        using SolidBrush fill = new(UiPalette.SurfaceRaised);
        using Pen border = new(ContainsFocus ? UiPalette.Accent : UiPalette.Border);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.Enter -= OnEditorEnter;
            _editor.Leave -= OnEditorLeave;
            _editor.KeyDown -= OnEditorKeyDown;
            _editor.KeyPress -= OnEditorKeyPress;
        }

        base.Dispose(disposing);
    }

    private void OnEditorEnter(object? sender, EventArgs e)
    {
        _editor.SelectAll();
        Invalidate();
    }

    private void OnEditorLeave(object? sender, EventArgs e)
    {
        CommitText();
        Invalidate();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is not Keys.Up and not Keys.Down)
        {
            return;
        }

        CommitText();
        Value = _value.AddMinutes(e.KeyCode == Keys.Up ? 5 : -5);
        _editor.SelectAll();
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private static void OnEditorKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar) &&
            !char.IsDigit(e.KeyChar) &&
            e.KeyChar is not '-' and not ':' and not ' ')
        {
            e.Handled = true;
        }
    }

    private void CommitText()
    {
        if (UiInputPolicy.TryParseDateTime(
                _editor.Text,
                _minimum,
                _maximum,
                out DateTime parsed))
        {
            Value = parsed;
            return;
        }

        UpdateText();
    }

    private void UpdateText()
    {
        if (_updatingText || _editor is null)
        {
            return;
        }

        _updatingText = true;
        _editor.Text = _value.ToString(
            UiInputPolicy.DateTimeFormat,
            CultureInfo.InvariantCulture);
        _updatingText = false;
    }

    private void LayoutEditor()
    {
        if (_editor is null)
        {
            return;
        }

        int preferredHeight = Math.Min(_editor.PreferredHeight, Math.Max(1, Height - UiLayoutMetrics.Pixels(this, 8)));
        _editor.Bounds = new Rectangle(
            UiLayoutMetrics.Pixels(this, 10),
            Math.Max(UiLayoutMetrics.Pixels(this, 4), (Height - preferredHeight) / 2),
            Math.Max(1, Width - UiLayoutMetrics.Pixels(this, 20)),
            preferredHeight);
    }
}

public sealed class ModernMultilineInput : UserControl
{
    private readonly TextBox _editor;

    public ModernMultilineInput()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        BackColor = Color.Transparent;
        ForeColor = UiPalette.TextPrimary;
        _editor = new TextBox
        {
            AcceptsReturn = true,
            BackColor = UiPalette.SurfaceRaised,
            BorderStyle = BorderStyle.None,
            ForeColor = UiPalette.TextPrimary,
            Multiline = true,
            ScrollBars = ScrollBars.None,
            ShortcutsEnabled = true,
        };
        _editor.TextChanged += OnEditorTextChanged;
        _editor.Enter += OnEditorFocusChanged;
        _editor.Leave += OnEditorFocusChanged;
        Controls.Add(_editor);
        LayoutEditor();
    }

    [AllowNull]
    public override string Text
    {
        get => _editor?.Text ?? base.Text;
        set
        {
            if (_editor is null)
            {
                base.Text = value ?? string.Empty;
            }
            else
            {
                _editor.Text = value ?? string.Empty;
            }
        }
    }

    [DefaultValue(32767)]
    public int MaxLength
    {
        get => _editor.MaxLength;
        set => _editor.MaxLength = Math.Max(0, value);
    }

    [DefaultValue("")]
    public string PlaceholderText
    {
        get => _editor.PlaceholderText;
        set => _editor.PlaceholderText = value;
    }

    [Browsable(false)]
    public int TextLength => _editor.TextLength;

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (_editor is not null)
        {
            _editor.Font = Font;
            LayoutEditor();
        }
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        if (_editor is not null)
        {
            _editor.ForeColor = ForeColor;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutEditor();
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        _editor.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Rectangle bounds = new(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        using GraphicsPath path = SurfacePanel.CreateRoundedRectangle(bounds, 10);
        using SolidBrush fill = new(UiPalette.SurfaceRaised);
        using Pen border = new(ContainsFocus ? UiPalette.Accent : UiPalette.Border);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.TextChanged -= OnEditorTextChanged;
            _editor.Enter -= OnEditorFocusChanged;
            _editor.Leave -= OnEditorFocusChanged;
        }

        base.Dispose(disposing);
    }

    private void OnEditorTextChanged(object? sender, EventArgs e) =>
        base.OnTextChanged(e);

    private void OnEditorFocusChanged(object? sender, EventArgs e) => Invalidate();

    private void LayoutEditor()
    {
        if (_editor is null)
        {
            return;
        }

        _editor.Bounds = new Rectangle(
            UiLayoutMetrics.Pixels(this, 10),
            UiLayoutMetrics.Pixels(this, 8),
            Math.Max(1, Width - UiLayoutMetrics.Pixels(this, 20)),
            Math.Max(1, Height - UiLayoutMetrics.Pixels(this, 16)));
    }
}

public sealed class ModernToggle : CheckBox
{
    private bool _hovered;
    private bool _pressed;

    public ModernToggle()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        Appearance = Appearance.Button;
        AutoSize = false;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = new Font(UiTheme.FontFamily, 9F, FontStyle.Regular, GraphicsUnit.Point);
        ForeColor = UiPalette.TextPrimary;
        TextAlign = ContentAlignment.MiddleCenter;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventargs);
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(eventargs);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        Invalidate();
        base.OnCheckedChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (ClientSize.Width < 32 || ClientSize.Height < 20)
        {
            return;
        }

        Graphics graphics = pevent.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(UiPalette.Surface);
        Rectangle track = new(1, 2, ClientSize.Width - 3, ClientSize.Height - 5);
        int radius = track.Height / 2;
        using GraphicsPath trackPath = SurfacePanel.CreateRoundedRectangle(track, radius);
        using SolidBrush background = new(GetTrackColor());
        using Pen border = new(GetTrackBorderColor());
        graphics.FillPath(background, trackPath);
        graphics.DrawPath(border, trackPath);

        int knobDiameter = Math.Max(UiLayoutMetrics.Pixels(this, 16), track.Height - UiLayoutMetrics.Pixels(this, 6));
        int knobY = track.Y + ((track.Height - knobDiameter) / 2);
        int knobX = Checked
            ? track.Right - knobDiameter - UiLayoutMetrics.Pixels(this, 3)
            : track.Left + UiLayoutMetrics.Pixels(this, 3);
        Rectangle knob = new(knobX, knobY, knobDiameter, knobDiameter);
        using SolidBrush knobBrush = new(
            Enabled ? UiPalette.TextPrimary : UiPalette.TextMuted);
        graphics.FillEllipse(knobBrush, knob);

        Rectangle textBounds = Checked
            ? new(track.Left + UiLayoutMetrics.Pixels(this, 6), track.Y,
                Math.Max(1, knob.Left - track.Left - UiLayoutMetrics.Pixels(this, 8)), track.Height)
            : new(knob.Right + UiLayoutMetrics.Pixels(this, 3), track.Y,
                Math.Max(1, track.Right - knob.Right - UiLayoutMetrics.Pixels(this, 6)), track.Height);
        using Font stateFont = new(
            UiTheme.FontFamily,
            UiLayoutMetrics.Pixels(this, 10),
            FontStyle.Bold,
            GraphicsUnit.Pixel);
        TextRenderer.DrawText(
            graphics,
            Checked ? "开" : "关",
            stateFont,
            textBounds,
            Enabled ? UiPalette.TextPrimary : UiPalette.TextMuted,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPrefix);
    }

    private Color GetTrackColor()
    {
        if (!Enabled)
        {
            return UiPalette.Surface;
        }

        if (Checked)
        {
            return _pressed
                ? UiPalette.SwitchOnPressed
                : _hovered
                    ? UiPalette.SwitchOnHover
                    : UiPalette.SwitchOn;
        }

        return _pressed
            ? UiPalette.Sidebar
            : _hovered
                ? UiPalette.SurfaceHover
                : UiPalette.SurfaceRaised;
    }

    private Color GetTrackBorderColor()
    {
        if (!Enabled)
        {
            return UiPalette.BorderSoft;
        }

        if (Checked)
        {
            return UiPalette.Success;
        }

        return _hovered ? UiPalette.Border : UiPalette.BorderSoft;
    }
}

/// <summary>
/// Owner-drawn rounded button used inside the fixed step and segment selectors.
/// It paints its own fill, border, hover, pressed, disabled and selected states.
/// </summary>
internal class ModernStepButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private bool _selected;

    public ModernStepButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = new Font(UiTheme.FontFamily, 10.5F, FontStyle.Bold, GraphicsUnit.Point);
        ForeColor = UiPalette.TextPrimary;
        Margin = Padding.Empty;
        UseVisualStyleBackColor = false;
    }

    [DefaultValue(1F)]
    public float OutlineWidth { get; set; } = 1;

    [DefaultValue(UiIcon.None)]
    public UiIcon Icon { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(eventargs);
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(eventargs);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (ClientSize.Width < 2 || ClientSize.Height < 2)
        {
            return;
        }

        Graphics graphics = pevent.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Parent is SurfacePanel panel ? panel.SurfaceColor : Parent?.BackColor ?? BackColor);
        Rectangle bounds = new(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        int radius = Math.Min(10, Math.Min(ClientSize.Width, ClientSize.Height) / 2);
        if (Enabled && _hovered && !_selected)
        {
            using GraphicsPath glowPath = SurfacePanel.CreateRoundedRectangle(bounds, radius);
            using Pen glow = new(UiPalette.LightGlow, 2F);
            graphics.DrawPath(glow, glowPath);
        }

        using GraphicsPath path = SurfacePanel.CreateRoundedRectangle(bounds, radius);
        using SolidBrush background = new(GetFillColor());
        using Pen border = new(GetBorderColor(), OutlineWidth == 1 ? 1 : UiLayoutMetrics.Pixels(this, (int)OutlineWidth)) { Alignment = PenAlignment.Inset };
        graphics.FillPath(background, path);
        graphics.DrawPath(border, path);
        if (Icon != UiIcon.None)
        {
            int iconSize = UiLayoutMetrics.Pixels(this, 17);
            Rectangle iconBounds = new(
                (Width - iconSize) / 2,
                (Height - iconSize) / 2,
                iconSize,
                iconSize);
            UiIcons.Draw(
                graphics,
                Icon,
                iconBounds,
                Enabled ? ForeColor : UiPalette.TextMuted,
                2F);
        }
        else
        {
            TextRenderer.DrawText(
                graphics,
                Text,
                Font,
                bounds,
                Enabled ? ForeColor : UiPalette.TextMuted,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }

    private Color GetFillColor()
    {
        if (!Enabled)
        {
            return UiPalette.Surface;
        }

        if (Selected)
        {
            return _pressed
                ? UiPalette.AccentDark
                : _hovered
                    ? UiPalette.AccentHover
                    : UiPalette.Accent;
        }

        return _pressed
            ? UiPalette.Sidebar
            : _hovered
                ? UiPalette.SurfaceHover
                : UiPalette.SurfaceRaised;
    }

    private Color GetBorderColor()
    {
        if (!Enabled)
        {
            return UiPalette.BorderSoft;
        }

        return Selected ? UiPalette.Accent : UiPalette.BorderSoft;
    }
}

/// <summary>
/// Owner-drawn label with a rounded background, used as the value display
/// inside the fixed step selector instead of a square fixed border.
/// </summary>
internal sealed class RoundedValueLabel : Label
{
    public RoundedValueLabel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        if (ClientSize.Width < 2 || ClientSize.Height < 2)
        {
            return;
        }

        Graphics graphics = pevent.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        int radius = Math.Min(9, Math.Min(ClientSize.Width, ClientSize.Height) / 2);
        using GraphicsPath path = SurfacePanel.CreateRoundedRectangle(bounds, radius);
        using SolidBrush background = new(UiPalette.Sidebar);
        using Pen border = new(UiPalette.BorderSoft);
        graphics.FillPath(background, path);
        graphics.DrawPath(border, path);
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            bounds,
            Enabled ? ForeColor : UiPalette.TextMuted,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }
}
