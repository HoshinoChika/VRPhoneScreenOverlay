using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal static class TranslationPanelRenderer
{
    public static TranslationPanelFrame Render(IReadOnlyList<TranslationRecord> records, ISet<Guid> expanded,
        string status, bool busy, int scroll, long revision, TranslationPanelAction? pressed = null, bool shutterSound = true)
    {
        using var bitmap = new Bitmap(OpenVrTranslationChannel.Width, OpenVrTranslationChannel.Height, PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(UiPalette.Window);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var titleFont = new Font("Microsoft YaHei UI", 23, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textFont = new Font("Microsoft YaHei UI", 22, FontStyle.Regular, GraphicsUnit.Pixel);
        using var buttonBrush = new SolidBrush(UiPalette.SurfaceRaised);
        using var cardBrush = new SolidBrush(UiPalette.Surface);
        using var primaryBrush = new SolidBrush(UiPalette.AccentDark);
        using var textBrush = new SolidBrush(UiPalette.TextPrimary);
        using var secondaryBrush = new SolidBrush(UiPalette.TextSecondary);
        using var infoBrush = new SolidBrush(UiPalette.Info);
        using var borderPen = new Pen(UiPalette.BorderSoft, 1.5f);
        var hits = new List<TranslationPanelHit>();
        graphics.DrawString("翻译助手", titleFont, textBrush, 24, 24);
        graphics.DrawString("截图声", textFont, secondaryBrush, 432, 30);
        using (GraphicsPath track = SurfacePanel.CreateRoundedRectangle(new Rectangle(526, 24, 94, 38), 19))
        using (var trackBrush = new SolidBrush(shutterSound ? UiPalette.SwitchOn : UiPalette.ToggleOff))
        {
            graphics.FillPath(trackBrush, track);
            graphics.FillEllipse(textBrush, shutterSound ? 584 : 530, 28, 30, 30);
        }
        hits.Add(new(428, 16, 202, 52, TranslationPanelAction.SoundToggle));
        Button("关闭", 650, 16, 94, TranslationPanelAction.Close);
        Button("答题并翻译", 24, 76, 230, TranslationPanelAction.Answer, !busy);
        Button("翻译", 268, 76, 230, TranslationPanelAction.Translate, !busy);
        Button("解释画面", 512, 76, 232, TranslationPanelAction.Explain, !busy);
        graphics.DrawString(status, textFont, infoBrush, new RectangleF(24, 148, 720, 86));
        graphics.SetClip(new Rectangle(16, 240, 736, 704));
        int[] bodyHeights = records.Select(record => expanded.Contains(record.Id)
            ? (int)Math.Ceiling(graphics.MeasureString(record.Text, textFont, 688).Height) + 24 : 0).ToArray();
        int maximumScroll = Math.Max(0, 248 + bodyHeights.Sum(height => 72 + height) - 944);
        scroll = Math.Clamp(scroll, 0, maximumScroll);
        int y = 248 - scroll;
        for (int index = 0; index < records.Count; index++)
        {
            TranslationRecord record = records[index];
            bool open = expanded.Contains(record.Id);
            int bodyHeight = bodyHeights[index];
            int height = 72 + bodyHeight;
            if (y + height >= 240 && y < 944)
            {
                using GraphicsPath card = SurfacePanel.CreateRoundedRectangle(new Rectangle(24, y, 720, height - 8), 12);
                graphics.FillPath(cardBrush, card);
                graphics.DrawPath(borderPen, card);
                string heading = (open ? "▼ " : "▶ ") + record.CreatedAt.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) + "  " + record.Title;
                graphics.DrawString(heading, titleFont, textBrush, new RectangleF(40, y + 12, 680, 48));
                int top = Math.Max(240, y);
                int bottom = Math.Min(944, y + 64);
                if (bottom > top) { hits.Add(new(24, top, 720, bottom - top, TranslationPanelAction.ToggleRecord, index)); }
                if (open) { graphics.DrawString(record.Text, textFont, textBrush, new RectangleF(40, y + 68, 688, bodyHeight)); }
            }
            y += height;
        }
        graphics.ResetClip();
        PanelScrollFooter.Draw(graphics, textFont, scroll, maximumScroll, 704, $"历史 {records.Count} 条",
            pressed == TranslationPanelAction.ScrollUp, pressed == TranslationPanelAction.ScrollDown);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[bitmap.Width * bitmap.Height * 4];
        try
        {
            for (int row = 0; row < bitmap.Height; row++) { Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * bitmap.Width * 4, bitmap.Width * 4); }
        }
        finally { bitmap.UnlockBits(data); }
        for (int i = 0; i < pixels.Length; i += 4) { (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]); }
        Presentation.RoundedPanelGeometry.Apply(pixels, bitmap.Width, bitmap.Height);
        return new(pixels, hits, revision, maximumScroll, scroll);

        void Button(string text, int x, int top, int width, TranslationPanelAction action, bool enabled = true)
        {
            using GraphicsPath shape = SurfacePanel.CreateRoundedRectangle(new Rectangle(x, top, width, 52), 10);
            graphics.FillPath(!enabled ? cardBrush : action == pressed ? primaryBrush : buttonBrush, shape);
            graphics.DrawPath(borderPen, shape);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString(text, textFont, enabled ? textBrush : secondaryBrush, new RectangleF(x, top, width, 52), format);
            if (enabled) { hits.Add(new(x, top, width, 52, action)); }
        }
    }
}
