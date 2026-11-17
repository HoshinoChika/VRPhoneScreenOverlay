namespace VRPhoneScreenOverlay.App;

internal static class PanelScrollFooter
{
    public static void Draw(Graphics graphics, Font font, int scroll, int maximum, int viewport,
        string count, bool upPressed, bool downPressed)
    {
        int top = viewport == 740 ? 196 : 240;
        using var trackBrush = new SolidBrush(UiPalette.SurfaceRaised);
        using var thumbBrush = new SolidBrush(maximum == 0 ? UiPalette.BorderSoft : UiPalette.Info);
        using var track = SurfacePanel.CreateRoundedRectangle(new Rectangle(750, top, 6, viewport), 3);
        graphics.FillPath(trackBrush, track);
        int height = maximum == 0 ? viewport : Math.Max(28, (int)((long)viewport * viewport / (viewport + maximum)));
        int y = top + (maximum == 0 ? 0 : (int)((long)(viewport - height) * scroll / maximum));
        using var thumb = SurfacePanel.CreateRoundedRectangle(new Rectangle(750, y, 6, height), 3);
        graphics.FillPath(thumbBrush, thumb);
        using var muted = new SolidBrush(UiPalette.TextSecondary);
        using var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        graphics.DrawString(count, font, muted, new RectangleF(24, 956, 720, 52), format);
    }
}
