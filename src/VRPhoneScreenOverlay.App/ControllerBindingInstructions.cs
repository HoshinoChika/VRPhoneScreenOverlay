using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

public sealed class ControllerBindingInstructions : UserControl
{
    private static readonly string[] _actions =
        ["抓握", "触屏点击", "缩放 / 滚动", "返回", "桌面", "最近任务", "控制栏", "截屏", "隐藏后唤回", "空间拖拽", "重置空间"];
    private readonly SurfacePanel _phonePanel = new() { Name = "phoneInstructionsPanel", SurfaceColor = UiPalette.SurfaceRaised, BorderColor = UiPalette.BorderSoft, CornerRadius = 10, Margin = Padding.Empty };
    private readonly SurfacePanel _spacePanel = new() { Name = "playspaceInstructionsPanel", SurfaceColor = UiPalette.SurfaceRaised, BorderColor = UiPalette.BorderSoft, CornerRadius = 10, Margin = Padding.Empty };
    private readonly TableLayoutPanel _phoneRows = CreateRows();
    private readonly TableLayoutPanel _spaceRows = CreateRows();
    private bool _layingOut;
    private int _contentRevision;
    private LayoutInputs? _lastLayout;
    private (string Action, string Inputs)[]? _shownLines;

    private readonly record struct LayoutInputs(Size Size, int Dpi, string FontName, float FontSize, GraphicsUnit FontUnit, FontStyle FontStyle,
        int ContentRevision, Size PhonePanel, Size SpacePanel, Size PhoneRows, Size SpaceRows);

    private LayoutInputs CurrentLayoutInputs() => new(Size, DeviceDpi, Font.Name, Font.Size, Font.Unit, Font.Style, _contentRevision,
        _phonePanel.Size, _spacePanel.Size, _phoneRows.Size, _spaceRows.Size);

    public ControllerBindingInstructions()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        AutoScroll = true;
        BackColor = UiPalette.Surface;
        _phonePanel.Controls.Add(_phoneRows);
        _spacePanel.Controls.Add(_spaceRows);
        Controls.Add(_phonePanel);
        Controls.Add(_spacePanel);
        LayoutGroups();
    }

    internal void SetGuide(ControllerBindingGuide guide)
    {
        (string Action, string Inputs)[] lines = guide.Entries.Count == 0 ? [("", guide.Message)] : BuildLines(guide).ToArray();
        if (_shownLines is not null && _shownLines.SequenceEqual(lines)) { return; }
        _shownLines = lines;
        _contentRevision++;
        SuspendLayout();
        _phonePanel.SuspendLayout();
        _spacePanel.SuspendLayout();
        SetRows(_phoneRows, lines.Where(value => value.Action is not "空间拖拽" and not "重置空间").ToArray());
        SetRows(_spaceRows, lines.Where(value => value.Action is "空间拖拽" or "重置空间").ToArray());
        _phonePanel.ResumeLayout(false);
        _spacePanel.ResumeLayout(false);
        ResumeLayout(false);
        LayoutGroups();
    }

    private static TableLayoutPanel CreateRows()
    {
        TableLayoutPanel rows = new() { AutoSize = false, ColumnCount = 2, Padding = Padding.Empty, Margin = Padding.Empty };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        return rows;
    }

    private static void SetRows(TableLayoutPanel rows, (string Action, string Inputs)[] lines)
    {
        rows.SuspendLayout();
        while (rows.RowCount > lines.Length)
        {
            int row = rows.RowCount - 1;
            for (int column = 0; column < 2; column++) { rows.GetControlFromPosition(column, row)?.Dispose(); }
            rows.RowStyles.RemoveAt(row);
            rows.RowCount--;
        }
        while (rows.RowCount < lines.Length) { AddRow(rows, "", ""); }
        for (int row = 0; row < lines.Length; row++)
        {
            (string action, string inputs) = lines[row];
            if (rows.GetControlFromPosition(0, row) is Label name) { name.Text = action.Length > 0 ? action + "：" : ""; }
            if (rows.GetControlFromPosition(1, row) is Label value) { value.Text = inputs; }
        }
        rows.ResumeLayout(false);
    }

    private static void AddRow(TableLayoutPanel rows, string action, string inputs)
    {
        int row = rows.RowCount++;
        rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rows.Controls.Add(CreateLabel(action, UiPalette.TextSecondary), 0, row);
        rows.Controls.Add(CreateLabel(inputs, UiPalette.TextPrimary), 1, row);
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); LayoutGroups(); }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_layingOut) { return; }
        LayoutGroups();
        base.OnLayout(e);
    }

    private void LayoutGroups()
    {
        if (_layingOut || _phoneRows is null || _spaceRows is null) { return; }
        LayoutInputs inputs = CurrentLayoutInputs();
        if (_lastLayout == inputs) { return; }
        _layingOut = true;
        try
        {
            int inset = UiLayoutMetrics.Pixels(this, 12);
            int padding = UiLayoutMetrics.Pixels(this, 6);
            int gap = UiLayoutMetrics.Pixels(this, 8);
            Point previousScroll = AutoScrollPosition;
            // Reset cached native scroll extents before measuring the final font
            // and child bounds. Reserve vertical space only when it is needed.
            AutoScroll = false;
            AutoScrollMinSize = Size.Empty;
            HorizontalScroll.Visible = false;
            VerticalScroll.Visible = false;
            int width = Math.Max(1, Width - UiLayoutMetrics.Pixels(this, 2));
            _spacePanel.Visible = _spaceRows.RowCount > 0;
            int contentHeight = Arrange(width);
            bool scrolling = contentHeight > Height;
            if (scrolling) { contentHeight = Arrange(Math.Max(1, width - SystemInformation.VerticalScrollBarWidth)); }
            AutoScrollMinSize = scrolling ? new Size(0, contentHeight) : Size.Empty;
            AutoScroll = scrolling;
            if (scrolling) { AutoScrollPosition = new Point(0, Math.Max(0, -previousScroll.Y)); }

            int Arrange(int availableWidth)
            {
                int y = 0;
                foreach ((SurfacePanel panel, TableLayoutPanel rows) in new[] { (_phonePanel, _phoneRows), (_spacePanel, _spaceRows) })
                {
                    if (rows == _spaceRows && rows.RowCount == 0) { continue; }
                    rows.Width = Math.Max(1, availableWidth - inset * 2);
                    int height = 0;
                    for (int row = 0; row < rows.RowCount; row++)
                    {
                        int rowHeight = UiLayoutMetrics.Pixels(this, 23);
                        for (int column = 0; column < 2; column++)
                        {
                            if (rows.GetControlFromPosition(column, row) is not Label label) { continue; }
                            int columnWidth = column == 0 ? rows.Width / 4 : rows.Width - rows.Width / 4;
                            Size measured = TextRenderer.MeasureText(label.Text, label.Font,
                                new Size(Math.Max(1, columnWidth - label.Padding.Horizontal), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                            rowHeight = Math.Max(rowHeight, measured.Height + label.Padding.Vertical);
                        }
                        rows.RowStyles[row].SizeType = SizeType.Absolute;
                        rows.RowStyles[row].Height = rowHeight;
                        height += rowHeight;
                    }
                    rows.Bounds = new Rectangle(inset, padding, rows.Width, height);
                    rows.PerformLayout();
                    panel.Bounds = new Rectangle(0, y, availableWidth, height + padding * 2);
                    y = panel.Bottom + gap;
                }
                return Math.Max(0, y - gap);
            }
        }
        finally { _layingOut = false; }
        _lastLayout = CurrentLayoutInputs();
    }

    private static Label CreateLabel(string text, Color color) => new()
    {
        Text = text,
        ForeColor = color,
        AutoSize = true,
        Dock = DockStyle.Fill,
        MinimumSize = new Size(0, 23),
        Padding = new Padding(0, 2, 0, 2),
        Margin = Padding.Empty,
        UseMnemonic = false,
    };

    internal static IReadOnlyList<(string Action, string Inputs)> BuildLines(ControllerBindingGuide guide) =>
        _actions
            .Select(action => (action,
                FormatActionInputs(action, guide.Entries))).ToArray();

    private static string FormatActionInputs(string action, IReadOnlyList<ControllerBindingEntry> entries)
    {
        string inputs = FormatInputs(entries.Where(entry => entry.Action == action));
        if (action == "重置空间" && inputs != "未绑定") { return inputs + " · 按所选重置方式"; }
        return action == "隐藏后唤回" && inputs != "未绑定"
            ? inputs + " → 直接显示手机"
            : inputs;
    }

    private static string FormatInputs(IEnumerable<ControllerBindingEntry> entries)
    {
        string[] inputs = entries.Where(entry => entry.Mode != "unbound" && entry.InputPath.Length > 0)
            .Select(entry => (entry.DevicePath.EndsWith("/left", StringComparison.Ordinal) ? "左手 " :
                entry.DevicePath.EndsWith("/right", StringComparison.Ordinal) ? "右手 " : "") +
                InputLabel(entry.InputPath) +
                (entry.Action == "隐藏后唤回" ? "（长按 2 秒）" : entry.Slot == "click" && entry.InputPath.Split('/')[^1] is "thumbstick" or "joystick" or "trackpad"
                    ? "（按下）" : SlotLabel(entry.Slot))).Distinct().ToArray();
        return inputs.Length == 0 ? "未绑定" : string.Join(" / ", inputs);
    }

    private static string InputLabel(string path) => path.Split('/')[^1].ToLowerInvariant() switch
    {
        "trigger" => "扳机",
        "grip" => "握把",
        "thumbstick" or "joystick" => "摇杆",
        "trackpad" => "触控板",
        "application_menu" or "menu" => "菜单键",
        "system" => "系统键",
        "a" => "A 键",
        "b" => "B 键",
        "x" => "X 键",
        "y" => "Y 键",
        _ => path.Split('/')[^1],
    };

    private static string SlotLabel(string slot) => slot.ToLowerInvariant() switch
    {
        "click" => "",
        "long" => "（长按）",
        "double" => "（双击）",
        "position" => "（方向）",
        "touch" => "（触摸）",
        "pull" => "（扣动）",
        "force" => "（用力按压）",
        "value" => "（力度）",
        "" => "",
        _ => $"（{slot}）",
    };
}
