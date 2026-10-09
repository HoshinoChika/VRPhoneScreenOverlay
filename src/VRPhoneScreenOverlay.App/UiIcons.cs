using System.Drawing.Drawing2D;

namespace VRPhoneScreenOverlay.App;

public enum UiIcon
{
    None,
    AppMark,
    Home,
    Settings,
    Globe,
    Info,
    Usb,
    Phone,
    Video,
    Audio,
    Control,
    Vr,
    Download,
    Upload,
    Undo,
    CheckMark,
    Minimize,
    Close,
    Plus,
    Minus,
    Play,
    Pause,
    VrController,
    LoadingDots,
    Steam,
    SteamVr,
    Puzzle,
    ChevronLeft,
    ChevronRight,
}

/// <summary>
/// Resolution-independent application iconography. Every icon is drawn from the
/// same 24-unit rounded-line grid so it stays crisp at desktop and VR scaling.
/// </summary>
internal static class UiIcons
{
    public const UiIcon AppMark = UiIcon.AppMark;
    public const UiIcon Home = UiIcon.Home;
    public const UiIcon Settings = UiIcon.Settings;
    public const UiIcon Globe = UiIcon.Globe;
    public const UiIcon Info = UiIcon.Info;
    public const UiIcon Usb = UiIcon.Usb;
    public const UiIcon Phone = UiIcon.Phone;
    public const UiIcon Video = UiIcon.Video;
    public const UiIcon Audio = UiIcon.Audio;
    public const UiIcon Control = UiIcon.Control;
    public const UiIcon Vr = UiIcon.Vr;
    public const UiIcon Download = UiIcon.Download;
    public const UiIcon Upload = UiIcon.Upload;
    public const UiIcon Undo = UiIcon.Undo;
    public const UiIcon CheckMark = UiIcon.CheckMark;
    public const UiIcon Minimize = UiIcon.Minimize;
    public const UiIcon Close = UiIcon.Close;
    public const UiIcon Plus = UiIcon.Plus;
    public const UiIcon Minus = UiIcon.Minus;
    public const UiIcon Play = UiIcon.Play;
    public const UiIcon Pause = UiIcon.Pause;
    public const UiIcon VrController = UiIcon.VrController;
    public const UiIcon LoadingDots = UiIcon.LoadingDots;
    public const UiIcon Steam = UiIcon.Steam;
    public const UiIcon SteamVr = UiIcon.SteamVr;
    public const UiIcon Puzzle = UiIcon.Puzzle;

    public static void Draw(
        Graphics graphics,
        UiIcon icon,
        Rectangle bounds,
        Color color,
        float strokeWidth = 1.9F)
    {
        if (icon == UiIcon.None || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        GraphicsState state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            const float visualGrid = 21F;
            float scale = Math.Min(bounds.Width, bounds.Height) / visualGrid;
            float offsetX = bounds.X + (bounds.Width / 2F) - (12F * scale);
            float offsetY = bounds.Y + (bounds.Height / 2F) - (12F * scale);
            graphics.TranslateTransform(offsetX, offsetY);
            graphics.ScaleTransform(scale, scale);

            using Pen pen = new(color, strokeWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            DrawNormalized(graphics, pen, icon);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static void DrawNormalized(Graphics graphics, Pen pen, UiIcon icon)
    {
        switch (icon)
        {
            case UiIcon.Puzzle:
                using (GraphicsPath piece = new())
                {
                    // A rounded tile with top/right tabs and left/bottom sockets.
                    // Keep the outline inside the same visual grid as the other icons.
                    piece.AddLine(5, 7, 8, 7);
                    piece.AddLine(8, 7, 8, 5.5F);
                    piece.AddBezier(8, 5.5F, 8, 2, 13, 2, 13, 5.5F);
                    piece.AddLine(13, 5.5F, 13, 7);
                    piece.AddLine(13, 7, 17, 7);
                    piece.AddArc(16, 7, 2, 2, 270, 90);
                    piece.AddLine(18, 8, 18, 10.5F);
                    piece.AddLine(18, 10.5F, 19.5F, 10.5F);
                    piece.AddBezier(19.5F, 10.5F, 23, 10.5F, 23, 15.5F, 19.5F, 15.5F);
                    piece.AddLine(19.5F, 15.5F, 18, 15.5F);
                    piece.AddLine(18, 15.5F, 18, 20);
                    piece.AddArc(16, 19, 2, 2, 0, 90);
                    piece.AddLine(17, 21, 13, 21);
                    piece.AddBezier(13, 21, 13, 17, 8, 17, 8, 21);
                    piece.AddLine(8, 21, 5, 21);
                    piece.AddArc(4, 19, 2, 2, 90, 90);
                    piece.AddLine(4, 20, 4, 16);
                    piece.AddBezier(4, 16, 8, 16, 8, 11, 4, 11);
                    piece.AddLine(4, 11, 4, 8);
                    piece.AddArc(4, 7, 2, 2, 180, 90);
                    piece.CloseFigure();
                    graphics.DrawPath(pen, piece);
                }
                break;
            case UiIcon.ChevronLeft:
                graphics.DrawLines(pen, [new PointF(15, 5), new PointF(8, 12), new PointF(15, 19)]);
                break;
            case UiIcon.ChevronRight:
                graphics.DrawLines(pen, [new PointF(9, 5), new PointF(16, 12), new PointF(9, 19)]);
                break;
            case UiIcon.AppMark:
                DrawRoundedRectangle(graphics, pen, 6F, 2.5F, 12F, 19F, 3F);
                graphics.DrawLine(pen, 9F, 5.5F, 15F, 5.5F);
                graphics.DrawArc(pen, 8.5F, 8F, 7F, 7F, 205F, 130F);
                graphics.DrawLine(pen, 8.5F, 12F, 8.5F, 15.5F);
                graphics.DrawLine(pen, 15.5F, 12F, 15.5F, 15.5F);
                graphics.DrawLine(pen, 11F, 18.5F, 13F, 18.5F);
                break;
            case UiIcon.Home:
                graphics.DrawLines(pen, new PointF[] { new(3.5F, 11F), new(12F, 4F), new(20.5F, 11F) });
                graphics.DrawLines(pen, new PointF[] { new(5.5F, 10F), new(5.5F, 20F), new(18.5F, 20F), new(18.5F, 10F) });
                graphics.DrawLines(pen, new PointF[] { new(9.5F, 20F), new(9.5F, 14F), new(14.5F, 14F), new(14.5F, 20F) });
                break;
            case UiIcon.Settings:
                graphics.DrawEllipse(pen, 8.5F, 8.5F, 7F, 7F);
                graphics.DrawEllipse(pen, 4F, 4F, 16F, 16F);
                for (int index = 0; index < 8; index++)
                {
                    double angle = index * Math.PI / 4D;
                    float x1 = 12F + ((float)Math.Cos(angle) * 8F);
                    float y1 = 12F + ((float)Math.Sin(angle) * 8F);
                    float x2 = 12F + ((float)Math.Cos(angle) * 10F);
                    float y2 = 12F + ((float)Math.Sin(angle) * 10F);
                    graphics.DrawLine(pen, x1, y1, x2, y2);
                }

                break;
            case UiIcon.Globe:
                graphics.DrawEllipse(pen, 3F, 3F, 18F, 18F);
                graphics.DrawEllipse(pen, 8F, 3F, 8F, 18F);
                graphics.DrawLine(pen, 3.5F, 9F, 20.5F, 9F);
                graphics.DrawLine(pen, 3.5F, 15F, 20.5F, 15F);
                break;
            case UiIcon.Info:
                graphics.DrawEllipse(pen, 3F, 3F, 18F, 18F);
                graphics.DrawLine(pen, 12F, 10.5F, 12F, 17F);
                graphics.DrawEllipse(pen, 11.5F, 6.5F, 1F, 1F);
                break;
            case UiIcon.Usb:
                graphics.DrawLine(pen, 12F, 3F, 12F, 17F);
                graphics.DrawLines(pen, new PointF[] { new(9F, 6F), new(12F, 3F), new(15F, 6F) });
                graphics.DrawLines(pen, new PointF[] { new(12F, 10F), new(7F, 10F), new(7F, 14F) });
                graphics.DrawEllipse(pen, 5.5F, 14F, 3F, 3F);
                graphics.DrawLines(pen, new PointF[] { new(12F, 13F), new(17F, 13F), new(17F, 9F) });
                graphics.DrawRectangle(pen, 15.5F, 6.5F, 3F, 3F);
                graphics.DrawLine(pen, 12F, 17F, 12F, 19F);
                graphics.DrawEllipse(pen, 10.5F, 19F, 3F, 3F);
                break;
            case UiIcon.Phone:
                DrawRoundedRectangle(graphics, pen, 6F, 2.5F, 12F, 19F, 2.5F);
                graphics.DrawLine(pen, 10F, 5.5F, 14F, 5.5F);
                graphics.DrawLine(pen, 11F, 18.5F, 13F, 18.5F);
                break;
            case UiIcon.Video:
                DrawRoundedRectangle(graphics, pen, 3F, 6F, 13F, 12F, 2.5F);
                graphics.DrawLines(pen, new PointF[] { new(16F, 10F), new(21F, 7.5F), new(21F, 16.5F), new(16F, 14F) });
                break;
            case UiIcon.Audio:
                graphics.DrawLines(pen, new PointF[] { new(4F, 10F), new(8F, 10F), new(12F, 6F), new(12F, 18F), new(8F, 14F), new(4F, 14F), new(4F, 10F) });
                graphics.DrawArc(pen, 12.5F, 8F, 5F, 8F, 285F, 150F);
                graphics.DrawArc(pen, 12F, 5F, 9F, 14F, 290F, 140F);
                break;
            case UiIcon.Control:
                using (GraphicsPath controller = new())
                {
                    controller.AddBezier(4F, 10F, 5F, 6F, 8F, 6F, 10F, 8F);
                    controller.AddLine(10F, 8F, 14F, 8F);
                    controller.AddBezier(14F, 8F, 16F, 6F, 19F, 6F, 20F, 10F);
                    controller.AddLine(20F, 10F, 21F, 16F);
                    controller.AddBezier(21F, 16F, 21.5F, 19F, 18.5F, 20F, 17F, 18F);
                    controller.AddLine(17F, 18F, 14.5F, 15.5F);
                    controller.AddLine(14.5F, 15.5F, 9.5F, 15.5F);
                    controller.AddLine(9.5F, 15.5F, 7F, 18F);
                    controller.AddBezier(7F, 18F, 5.5F, 20F, 2.5F, 19F, 3F, 16F);
                    controller.CloseFigure();
                    graphics.DrawPath(pen, controller);
                }

                graphics.DrawLine(pen, 7F, 11.5F, 11F, 11.5F);
                graphics.DrawLine(pen, 9F, 9.5F, 9F, 13.5F);
                graphics.DrawEllipse(pen, 15F, 10F, 1F, 1F);
                graphics.DrawEllipse(pen, 17.5F, 12.5F, 1F, 1F);
                break;
            case UiIcon.Vr:
            case UiIcon.SteamVr:
                using (GraphicsPath headset = new())
                {
                    headset.AddLines(new PointF[] { new(3F, 9F), new(4.5F, 17F), new(9F, 18F), new(11F, 14F), new(13F, 14F), new(15F, 18F), new(19.5F, 17F), new(21F, 9F) });
                    headset.AddBezier(21F, 9F, 18F, 6F, 6F, 6F, 3F, 9F);
                    headset.CloseFigure();
                    graphics.DrawPath(pen, headset);
                }

                graphics.DrawLine(pen, 5F, 10F, 19F, 10F);
                break;
            case UiIcon.Download:
            case UiIcon.Upload:
                bool upload = icon == UiIcon.Upload;
                float arrowStart = upload ? 17F : 5F;
                float arrowEnd = upload ? 6F : 16F;
                graphics.DrawLine(pen, 12F, arrowStart, 12F, arrowEnd);
                float headY = upload ? 9F : 13F;
                graphics.DrawLines(pen, new PointF[] { new(8.5F, headY), new(12F, arrowEnd), new(15.5F, headY) });
                graphics.DrawLines(pen, new PointF[] { new(5F, 17F), new(5F, 20F), new(19F, 20F), new(19F, 17F) });
                break;
            case UiIcon.Undo:
                graphics.DrawLines(pen, new PointF[] { new(9F, 7F), new(4F, 11F), new(9F, 15F) });
                graphics.DrawBezier(pen, 5F, 11F, 10F, 6F, 19F, 8F, 20F, 17F);
                break;
            case UiIcon.CheckMark:
                graphics.DrawLines(pen, new PointF[] { new(4F, 12.5F), new(9.5F, 18F), new(20F, 6F) });
                break;
            case UiIcon.Minimize:
                graphics.DrawLine(pen, 6F, 17F, 18F, 17F);
                break;
            case UiIcon.Close:
                graphics.DrawLine(pen, 6F, 6F, 18F, 18F);
                graphics.DrawLine(pen, 18F, 6F, 6F, 18F);
                break;
            case UiIcon.Plus:
                graphics.DrawLine(pen, 5F, 12F, 19F, 12F);
                graphics.DrawLine(pen, 12F, 5F, 12F, 19F);
                break;
            case UiIcon.Minus:
                graphics.DrawLine(pen, 5F, 12F, 19F, 12F);
                break;
            case UiIcon.Play:
                using (GraphicsPath play = new())
                {
                    play.AddLines(new PointF[]
                    {
                        new(7.5F, 5F),
                        new(19F, 12F),
                        new(7.5F, 19F),
                    });
                    play.CloseFigure();
                    graphics.DrawPath(pen, play);
                }

                break;
            case UiIcon.Pause:
                DrawRoundedRectangle(graphics, pen, 6F, 5F, 4F, 14F, 1F);
                DrawRoundedRectangle(graphics, pen, 14F, 5F, 4F, 14F, 1F);
                break;
            case UiIcon.VrController:
                graphics.DrawEllipse(pen, 4F, 3F, 11F, 10F);
                graphics.DrawLines(
                    pen,
                    new PointF[]
                    {
                        new(10F, 11F),
                        new(13.5F, 10F),
                        new(20F, 18F),
                        new(16.5F, 21F),
                        new(10F, 12.5F),
                    });
                graphics.DrawEllipse(pen, 7.5F, 6F, 3F, 3F);
                graphics.DrawLine(pen, 14.5F, 14F, 16.5F, 16F);
                break;
            case UiIcon.LoadingDots:
                using (SolidBrush dots = new(pen.Color))
                {
                    graphics.FillEllipse(dots, 4.5F, 10.5F, 3F, 3F);
                    graphics.FillEllipse(dots, 10.5F, 10.5F, 3F, 3F);
                    graphics.FillEllipse(dots, 16.5F, 10.5F, 3F, 3F);
                }

                break;
            case UiIcon.Steam:
                graphics.DrawEllipse(pen, 11F, 3F, 9F, 9F);
                graphics.DrawEllipse(pen, 13.5F, 5.5F, 4F, 4F);
                graphics.DrawEllipse(pen, 2.5F, 15F, 6F, 6F);
                graphics.DrawLine(pen, 7.5F, 17F, 12.5F, 14F);
                graphics.DrawLine(pen, 12.5F, 14F, 15F, 10F);
                break;
            case UiIcon.None:
            default:
                break;
        }
    }

    private static void DrawRoundedRectangle(
        Graphics graphics,
        Pen pen,
        float x,
        float y,
        float width,
        float height,
        float radius)
    {
        using GraphicsPath path = new();
        float diameter = radius * 2F;
        path.AddArc(x, y, diameter, diameter, 180F, 90F);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270F, 90F);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0F, 90F);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90F, 90F);
        path.CloseFigure();
        graphics.DrawPath(pen, path);
    }
}
