using System.Drawing.Drawing2D;

namespace VRPhoneScreenOverlay.App;

public sealed class RoundConnectionChoice : RadioButton
{
    public RoundConnectionChoice()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        CheckAlign = ContentAlignment.MiddleRight;
        AutoSize = false;
        UseMnemonic = false;
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        pevent.Graphics.Clear(UiPalette.Surface);
        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float padding = Height * .24f;
        float diameter = Height * .55f;
        RectangleF circle = new(Width - diameter - padding, (Height - diameter) / 2, diameter, diameter);
        using SolidBrush fill = new(Checked ? UiPalette.Accent : UiPalette.SurfaceRaised);
        using Pen border = new(Checked ? UiPalette.Accent : UiPalette.BorderSoft, 1.5f);
        using Pen frame = new(Checked ? UiPalette.Accent : UiPalette.BorderSoft, Math.Max(1, Height / 24f));
        using GraphicsPath outline = SurfacePanel.CreateRoundedRectangle(new Rectangle(1, 1, Width - 3, Height - 3), (int)(Height * .24f));
        using SolidBrush body = new(BackColor);
        pevent.Graphics.FillPath(body, outline);
        pevent.Graphics.DrawPath(frame, outline);
        pevent.Graphics.FillEllipse(fill, circle);
        pevent.Graphics.DrawEllipse(border, circle);
        if (Checked)
        {
            using Pen check = new(Color.White, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            pevent.Graphics.DrawLines(check, [new PointF(circle.Left + diameter * .25f, circle.Top + diameter * .52f),
                new PointF(circle.Left + diameter * .44f, circle.Top + diameter * .7f), new PointF(circle.Left + diameter * .77f, circle.Top + diameter * .3f)]);
        }
        TextRenderer.DrawText(pevent.Graphics, Text, Font, new Rectangle((int)padding, 0, Width - (int)(diameter + 3 * padding), Height),
            Enabled ? UiPalette.TextPrimary : UiPalette.TextSecondary, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        if (Focused) { ControlPaint.DrawFocusRectangle(pevent.Graphics, new Rectangle(0, 0, Width - 1, Height - 1)); }
    }
}
