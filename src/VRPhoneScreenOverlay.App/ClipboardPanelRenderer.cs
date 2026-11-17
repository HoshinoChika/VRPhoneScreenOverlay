using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal static class ClipboardPanelRenderer
{
    public static ClipboardPanelFrame Render(IReadOnlyList<PhoneClipboardEntry> entries, bool connected,
        ISet<Guid> expanded, int scroll, bool busy, ClipboardPanelHit? pressed, string? failure = null)
    {
        using var bitmap = new Bitmap(768, 1024, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(UiPalette.Window); g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var heading = new Font("Microsoft YaHei UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
        using var font = new Font("Microsoft YaHei UI", 22, FontStyle.Regular, GraphicsUnit.Pixel);
        using var small = new Font("Microsoft YaHei UI", 19, FontStyle.Regular, GraphicsUnit.Pixel);
        using var text = new SolidBrush(UiPalette.TextPrimary);
        using var secondary = new SolidBrush(UiPalette.TextSecondary);
        using var surface = new SolidBrush(UiPalette.Surface);
        using var raised = new SolidBrush(UiPalette.SurfaceRaised);
        using var accent = new SolidBrush(UiPalette.AccentDark);
        using var border = new Pen(UiPalette.BorderSoft, 1.5f);
        var hits = new List<ClipboardPanelHit>();
        g.DrawString("手机剪贴板", heading, text, 24, 24);
        Button("关闭", new(640, 18, 104, 52, ClipboardPanelAction.Close));
        g.DrawString(failure ?? (connected ? "同步中 · 仅接收本次连接后新复制的内容" : "等待手机控制连接 · 连接后复制内容即可显示"), small, secondary, new RectangleF(24, 88, 720, 48));
        g.DrawString("内容仅保留本次运行；点解析会将网址发送给解析服务。", small, secondary, new RectangleF(24, 134, 720, 52));
        g.SetClip(new Rectangle(16, 196, 736, 740));
        var layout = entries.Select(entry =>
        {
            string display = entry.Text.Length > 8000 ? entry.Text[..8000] + "\n…界面仅显示前8000字，复制保留完整内容" : entry.Text;
            int contentHeight = expanded.Contains(entry.Id) ? (int)Math.Ceiling(g.MeasureString(display, font, 432).Height) + 16 : 88;
            return (Entry: entry, Display: display, ContentHeight: contentHeight,
                Height: 62 + contentHeight + (entry.Status.Length > 0 ? 74 : 0));
        }).ToArray();
        int maximumScroll = Math.Max(0, 204 + layout.Sum(item => item.Height) - 936);
        scroll = Math.Clamp(scroll, 0, maximumScroll);
        int y = 204 - scroll;
        if (entries.Count == 0 && failure is null) { g.DrawString("还没有新内容\n在手机中点击分享 → 复制链接", font, secondary, new RectangleF(48, 260, 670, 120)); }
        foreach (var item in layout)
        {
            PhoneClipboardEntry entry = item.Entry;
            bool open = expanded.Contains(entry.Id);
            string display = item.Display;
            int contentHeight = item.ContentHeight;
            int height = item.Height;
            if (y + height >= 196 && y < 936)
            {
                using (GraphicsPath card = SurfacePanel.CreateRoundedRectangle(new Rectangle(24, y, 720, height - 10), 12))
                { g.FillPath(surface, card); g.DrawPath(border, card); }
                g.DrawString((open ? "▼ " : "▶ ") + entry.CopiedAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture), small, secondary, 40, y + 12);
                AddHit(new(24, y, 480, 48, ClipboardPanelAction.Expand, entry.Id));
                using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter })
                { g.DrawString(display, font, text, new RectangleF(40, y + 54, 432, contentHeight), format); }
                Button("复制", new(510, y + 48, 100, 48, ClipboardPanelAction.Copy, entry.Id), !busy);
                if (entry.Url is not null) { Button(entry.Busy ? "解析中" : "解析", new(622, y + 48, 100, 48, ClipboardPanelAction.Parse, entry.Id), !busy); }
                if (entry.Status.Length > 0) { g.DrawString(entry.Status, small, secondary, new RectangleF(40, y + 58 + contentHeight, 678, 60)); }
            }
            y += height;
        }
        g.ResetClip();
        PanelScrollFooter.Draw(g, small, scroll, maximumScroll, 740, $"本次 {entries.Count} / 50 条",
            pressed?.Action == ClipboardPanelAction.Up, pressed?.Action == ClipboardPanelAction.Down);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, 768, 1024), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[768 * 1024 * 4];
        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
        finally { bitmap.UnlockBits(data); }
        for (int i = 0; i < pixels.Length; i += 4) { (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]); }
        Presentation.RoundedPanelGeometry.Apply(pixels, bitmap.Width, bitmap.Height);
        return new(pixels, hits, maximumScroll, scroll);

        void AddHit(ClipboardPanelHit hit)
        {
            int top = Math.Max(196, hit.Y), bottom = Math.Min(936, hit.Y + hit.Height);
            if (bottom > top) { hits.Add(hit with { Y = top, Height = bottom - top }); }
        }
        void Button(string label, ClipboardPanelHit hit, bool enabled = true)
        {
            using GraphicsPath shape = SurfacePanel.CreateRoundedRectangle(new Rectangle(hit.X, hit.Y, hit.Width, hit.Height), 10);
            g.FillPath(!enabled ? surface : pressed?.EntryId == hit.EntryId && pressed?.Action == hit.Action ? accent : raised, shape);
            g.DrawPath(border, shape);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(label, font, enabled ? text : secondary, new RectangleF(hit.X, hit.Y, hit.Width, hit.Height), format);
            if (enabled)
            {
                if (hit.Action is ClipboardPanelAction.Copy or ClipboardPanelAction.Parse) { AddHit(hit); }
                else { hits.Add(hit); }
            }
        }
    }
}
