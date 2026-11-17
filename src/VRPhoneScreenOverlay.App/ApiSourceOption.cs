using System.Drawing.Drawing2D;

namespace VRPhoneScreenOverlay.App;

public sealed class ApiSourceOption : RadioButton
{
    public ApiSourceOption()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs pevent)
    {
        // RadioButton's native background can be black on the live themed HWND.
        pevent.Graphics.Clear(BackColor);
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int inset = Math.Max(10, Height / 3);
        int diameter = Math.Max(12, Font.Height - 2);
        using var border = new Pen(Checked ? UiPalette.Accent : UiPalette.BorderSoft, Math.Max(1.5f, diameter / 12f));
        var circle = new Rectangle(Width - inset - diameter, (Height - diameter) / 2, diameter, diameter);
        pevent.Graphics.DrawEllipse(border, circle);
        if (Checked)
        {
            int innerInset = Math.Max(4, diameter / 4);
            circle.Inflate(-innerInset, -innerInset);
            using var dot = new SolidBrush(UiPalette.Accent);
            pevent.Graphics.FillEllipse(dot, circle);
        }
        TextRenderer.DrawText(pevent.Graphics, Text, Font, new Rectangle(0, 0, Width - inset - diameter - 10, Height),
            Enabled ? UiPalette.TextPrimary : UiPalette.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        if (Focused) { ControlPaint.DrawFocusRectangle(pevent.Graphics, new Rectangle(4, 4, Width - 8, Height - 8)); }
    }
}
