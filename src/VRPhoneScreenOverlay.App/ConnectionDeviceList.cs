using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.App;

internal sealed record ConnectionDeviceListEntry(ManagedAndroidDevice Device)
{
    public override string ToString() => Device.DisplayName;
}

public sealed class ConnectionDeviceList : ListBox
{
    internal bool TryHitDevice(Point location, out ConnectionDeviceListEntry? entry)
    {
        int index = IndexFromPoint(location);
        entry = index >= 0 && index < Items.Count && GetItemRectangle(index).Contains(location)
            ? Items[index] as ConnectionDeviceListEntry : null;
        return entry is not null;
    }
    public ConnectionDeviceList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        IntegralHeight = false;
        HorizontalScrollbar = false;
        UpdateRowHeight();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateRowHeight();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRowHeight();
    }

    private void UpdateRowHeight() => ItemHeight = Math.Max(Font.Height * 2 + 2, ClientSize.Height / 4);

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) { return; }
        using SolidBrush background = new((e.State & DrawItemState.Selected) != 0 ? UiPalette.AccentDark : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        string name = Items[e.Index]?.ToString() ?? "";
        string detail = "";
        if (Items[e.Index] is ConnectionDeviceListEntry entry)
        {
            ManagedAndroidDevice device = entry.Device;
            if (device.IsSelected)
            {
                using var frame = SurfacePanel.CreateRoundedRectangle(new Rectangle(e.Bounds.X + 1, e.Bounds.Y + 1, e.Bounds.Width - 3, e.Bounds.Height - 2), 6);
                using SolidBrush connected = new(Color.FromArgb(40, UiPalette.Success));
                using Pen outline = new(UiPalette.Success, 1.5f);
                e.Graphics.FillPath(connected, frame);
                e.Graphics.DrawPath(outline, frame);
            }
            detail = AndroidDisplayNames.Transport(device.Transport) + " · " +
                (device.IsSelected ? "当前连接" : device.IsAvailable ? AndroidDisplayNames.DeviceStatus(device.Status) : "未连接") +
                (device.AutoConnect ? " · 自动连接" : "");
        }
        int padding = Math.Max(4, Font.Height / 3);
        Rectangle first = new(e.Bounds.X + padding, e.Bounds.Y + 1, e.Bounds.Width - padding * 2, Font.Height);
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, name, Font, first, UiPalette.TextPrimary, flags);
        using Font secondary = new(Font.FontFamily, Font.Size * .82f, FontStyle.Regular, Font.Unit);
        TextRenderer.DrawText(e.Graphics, detail, secondary,
            new Rectangle(first.X, first.Bottom, first.Width, e.Bounds.Bottom - first.Bottom), UiPalette.TextSecondary,
            flags);
        if ((e.State & DrawItemState.Focus) != 0) { e.DrawFocusRectangle(); }
    }
}
