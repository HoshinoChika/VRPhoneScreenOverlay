using System.ComponentModel;
using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.App;

[ToolboxItem(true)]
public sealed class BindingGuideSelectorControl : FixedStepSelector<string>
{
    public BindingGuideSelectorControl() : base(376)
    {
        Size = new Size(480, 38);
        SetStepIcons(UiIcon.ChevronLeft, UiIcon.ChevronRight);
    }
}

[ToolboxItem(true)]
public sealed class ControllerHandSelectorControl : FixedSegmentSelector<ControllerHandPreference>
{
    public ControllerHandSelectorControl()
        : base(240, 38)
    {
    }
}

[ToolboxItem(true)]
public sealed class UpdateChannelSelectorControl : FixedSegmentSelector<UpdateChannel>
{
    protected override float SegmentFontSize => 8F;

    public UpdateChannelSelectorControl()
        : base(209, 36)
    {
        Size = new Size(209, 36);
    }
}

[ToolboxItem(true)]
public sealed class DiagnosticIssueSelectorControl : FixedStepSelector<int>
{
    public DiagnosticIssueSelectorControl()
        : base(320)
    {
        Size = new Size(456, 38);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => TryGetSelectedValue(out int value) ? value : -1;
        set => _ = SelectValue(value);
    }

    public void SetItems(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        string[] values = items.ToArray();
        SetNodes(
            new[] { new FixedChoiceNode<int>(-1, "请选择问题类型") }
                .Concat(values.Select((text, index) => new FixedChoiceNode<int>(index, text))),
            -1);
    }
}

[ToolboxItem(true)]
public sealed class PlayspaceMultiplierSelectorControl : FixedStepSelector<int>
{
    public PlayspaceMultiplierSelectorControl()
        : base(96)
    {
        Size = new Size(106, 34);
    }
}

[ToolboxItem(true)]
public sealed class VideoResolutionSelectorControl : FixedStepSelector<VideoResolutionProfile>
{
    public VideoResolutionSelectorControl()
        : base(210)
    {
        Size = new Size(256, 38);
    }
}

[ToolboxItem(true)]
public sealed class VideoBitrateSelectorControl : FixedStepSelector<int>
{
    public VideoBitrateSelectorControl()
        : base(210)
    {
        Size = new Size(256, 38);
    }
}

[ToolboxItem(true)]
public sealed class VideoFrameRateSelectorControl : FixedStepSelector<int>
{
    public VideoFrameRateSelectorControl()
        : base(210)
    {
        Size = new Size(256, 38);
    }
}

[ToolboxItem(true)]
public sealed class PlayspaceResetSelectorControl : FixedSegmentSelector<bool>
{
    public PlayspaceResetSelectorControl() : base(400, 36)
    {
        Size = new Size(400, 36);
        SetNodes([new(false, "保留水平位置"), new(true, "全部偏移复位")], false);
    }
}

public enum WirelessPairingMethod { Usb, Scan, Code, Manual }

[ToolboxItem(true)]
public sealed class WirelessPairingMethodSelectorControl : UserControl
{
    private readonly RoundConnectionChoice[] _choices;
    public event EventHandler? SelectedValueChanged;

    public WirelessPairingMethodSelectorControl()
    {
        Size = new Size(558, 34);
        Font = new Font("Microsoft YaHei UI", 8.5F);
        BackColor = UiPalette.Surface;
        AutoScaleMode = AutoScaleMode.None;
        _choices = Enum.GetValues<WirelessPairingMethod>().Select((method, index) => new RoundConnectionChoice
        {
            Tag = method,
            Text = method switch
            {
                WirelessPairingMethod.Usb => "USB 连接",
                WirelessPairingMethod.Scan => "无线连接（扫码）",
                WirelessPairingMethod.Code => "无线连接（配对码）",
                _ => "无线连接（IP）",
            },
            Location = new Point(index == 0 ? 0 : index == 1 ? 116 : index == 2 ? 268 : 430, 0),
            Size = new Size(index == 0 ? 108 : index == 1 ? 144 : index == 2 ? 154 : 128, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Checked = method == WirelessPairingMethod.Usb,
            AccessibleName = method.ToString(),
            BackColor = UiPalette.SurfaceRaised,
        }).ToArray();
        foreach (RoundConnectionChoice choice in _choices)
        {
            Controls.Add(choice);
            choice.CheckedChanged += (_, _) => { if (choice.Checked) { SelectedValueChanged?.Invoke(this, EventArgs.Empty); } };
        }
    }

    public bool TryGetSelectedValue(out WirelessPairingMethod method)
    {
        method = (WirelessPairingMethod)(_choices.FirstOrDefault(choice => choice.Checked)?.Tag ?? WirelessPairingMethod.Usb);
        return true;
    }

    public bool SelectValue(WirelessPairingMethod method)
    {
        RoundConnectionChoice? choice = _choices.FirstOrDefault(value => Equals(value.Tag, method));
        if (choice is null) { return false; }
        choice.Checked = true;
        return true;
    }
}

[ToolboxItem(true)]
public sealed class VideoUploadModeSelectorControl : FixedSegmentSelector<int>
{
    public VideoUploadModeSelectorControl() : base(510, 38, segmentGap: 8)
    {
        SetNodes(new[] { new FixedChoiceNode<int>(0, "自动（推荐）"),
            new FixedChoiceNode<int>(1, "缓冲中转"), new FixedChoiceNode<int>(2, "直接提交") }, 0);
        string[] names = ["uploadAutoHelp", "uploadStagingHelp", "uploadDirectHelp"];
        string[] descriptions = ["优先缓冲中转，初始化兼容失败时尝试直接提交。", "通过中转纹理处理画面。", "直接交给显卡驱动处理。"];
        foreach (ModernStepButton button in Controls.OfType<ModernStepButton>())
        {
            int index = (int)button.Tag!;
            button.Name = "uploadOption" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ContextHelpIcon help = new() { Name = names[index], HelpText = descriptions[index], AccessibleName = button.Text + "说明" };
            button.Controls.Add(help);
            button.SizeChanged += (_, _) => ArrangeHelp(button, help);
            button.FontChanged += (_, _) => ArrangeHelp(button, help);
            ArrangeHelp(button, help);
        }
    }
    internal void ArrangeHelpIcons()
    {
        foreach (ModernStepButton button in Controls.OfType<ModernStepButton>())
        {
            foreach (ContextHelpIcon help in button.Controls.OfType<ContextHelpIcon>()) { ArrangeHelp(button, help); }
        }
    }
    private static void ArrangeHelp(Button button, ContextHelpIcon help)
    {
        int size = UiLayoutMetrics.Pixels(button, 22);
        int textWidth = TextRenderer.MeasureText(button.Text, button.Font, Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
        help.SetBounds((button.Width + textWidth) / 2 + UiLayoutMetrics.Pixels(button, 2),
            (button.Height - size) / 2, size, size);

    }
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => TryGetSelectedValue(out int value) ? value : 0;
        set => _ = SelectValue(value);
    }
}
