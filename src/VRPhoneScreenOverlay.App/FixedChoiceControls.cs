namespace VRPhoneScreenOverlay.App;

public sealed record FixedChoiceNode<T>(T Value, string DisplayText)
    where T : notnull;

public class FixedStepSelector<T> : UserControl
    where T : notnull
{
    private const int _buttonWidth = 44;
    private const int _gap = 8;
    private readonly ModernStepButton _decreaseButton;
    private readonly ModernStepButton _increaseButton;
    private readonly RoundedValueLabel _valueLabel;
    private FixedChoiceNode<T>[] _nodes = [];
    private int _selectedIndex;

    public FixedStepSelector(int valueWidth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(valueWidth, 80);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
        AutoSize = false;
        Size = new Size((_buttonWidth * 2) + (_gap * 2) + valueWidth, 40);
        Margin = Padding.Empty;
        BackColor = UiPalette.Surface;

        _decreaseButton = CreateStepButton(UiIcons.Minus, 0);
        _valueLabel = new RoundedValueLabel
        {
            AutoSize = false,
            Location = new Point(_buttonWidth + _gap, 0),
            Size = new Size(valueWidth, 40),
            ForeColor = UiPalette.TextPrimary,
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false,
        };
        _increaseButton = CreateStepButton(
            UiIcons.Plus,
            _buttonWidth + _gap + valueWidth + _gap);
        _decreaseButton.Click += OnDecreaseClicked;
        _increaseButton.Click += OnIncreaseClicked;
        Controls.Add(_decreaseButton);
        Controls.Add(_valueLabel);
        Controls.Add(_increaseButton);
        LayoutChildren();
    }

    public event EventHandler? SelectedValueChanged;

    protected void SetStepIcons(UiIcon previous, UiIcon next)
    {
        _decreaseButton.Icon = previous;
        _increaseButton.Icon = next;
    }

    public bool TryGetSelectedValue(out T value)
    {
        if (_nodes.Length == 0)
        {
            value = default!;
            return false;
        }

        value = _nodes[_selectedIndex].Value;
        return true;
    }

    public void SetNodes(IEnumerable<FixedChoiceNode<T>> nodes, T selectedValue)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        FixedChoiceNode<T>[] values = nodes.ToArray();
        ArgumentOutOfRangeException.ThrowIfZero(values.Length);
        _nodes = values;
        _selectedIndex = FindIndex(values, selectedValue);
        UpdateVisualState();
    }

    public bool SelectValue(T value)
    {
        int index = FindIndex(_nodes, value, useFirstWhenMissing: false);
        if (index < 0)
        {
            return false;
        }

        _selectedIndex = index;
        UpdateVisualState();
        return true;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        UpdateVisualState();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutChildren();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _decreaseButton.Click -= OnDecreaseClicked;
            _increaseButton.Click -= OnIncreaseClicked;
        }

        base.Dispose(disposing);
    }

    private static int FindIndex(
        FixedChoiceNode<T>[] nodes,
        T selectedValue,
        bool useFirstWhenMissing = true)
    {
        int index = Array.FindIndex(
            nodes,
            node => EqualityComparer<T>.Default.Equals(node.Value, selectedValue));
        return index >= 0 || !useFirstWhenMissing ? index : 0;
    }

    private static void SetControlEnabled(Control control, bool enabled)
    {
        if (control.Enabled != enabled)
        {
            control.Enabled = enabled;
        }
    }

    private static void SetControlText(Control control, string text)
    {
        if (!string.Equals(control.Text, text, StringComparison.Ordinal))
        {
            control.Text = text;
        }
    }

    private static ModernStepButton CreateStepButton(UiIcon icon, int left) => new()
    {
        AutoSize = false,
        Icon = icon,
        Location = new Point(left, 0),
        Size = new Size(_buttonWidth, 40),
    };

    private void LayoutChildren()
    {
        if (_decreaseButton is null || _increaseButton is null || _valueLabel is null)
        {
            return;
        }

        int gap = Math.Clamp(Width / 30, UiLayoutMetrics.Pixels(this, 3), UiLayoutMetrics.Pixels(this, _gap));
        int buttonWidth = Math.Clamp(Width / 5, UiLayoutMetrics.Pixels(this, 26), UiLayoutMetrics.Pixels(this, _buttonWidth));
        int valueWidth = Math.Max(UiLayoutMetrics.Pixels(this, 32), Width - (buttonWidth * 2) - (gap * 2));
        _decreaseButton.Bounds = new Rectangle(0, 0, buttonWidth, Height);
        _valueLabel.Bounds = new Rectangle(buttonWidth + gap, 0, valueWidth, Height);
        _increaseButton.Bounds = new Rectangle(buttonWidth + gap + valueWidth + gap, 0, buttonWidth, Height);
    }

    private void OnDecreaseClicked(object? sender, EventArgs eventArgs) =>
        SelectFromUser(_selectedIndex - 1);

    private void OnIncreaseClicked(object? sender, EventArgs eventArgs) =>
        SelectFromUser(_selectedIndex + 1);

    private void SelectFromUser(int index)
    {
        if (index < 0 || index >= _nodes.Length || index == _selectedIndex)
        {
            return;
        }

        _selectedIndex = index;
        UpdateVisualState();
        SelectedValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateVisualState()
    {
        bool hasSelection = _nodes.Length > 0 &&
            _selectedIndex >= 0 &&
            _selectedIndex < _nodes.Length;
        SetControlText(
            _valueLabel,
            hasSelection ? _nodes[_selectedIndex].DisplayText : string.Empty);
        SetControlEnabled(_decreaseButton, Enabled && hasSelection && _selectedIndex > 0);
        SetControlEnabled(
            _increaseButton,
            Enabled && hasSelection && _selectedIndex < _nodes.Length - 1);
    }
}

public class FixedSegmentSelector<T> : UserControl
    where T : notnull
{
    private readonly int _segmentWidth;
    private readonly int _segmentHeight;
    private readonly int _segmentGap;
    private readonly float _segmentBorderWidth;
    private FixedChoiceNode<T>[] _nodes = [];
    private ModernStepButton[] _buttons = [];
    private int _selectedIndex;

    public FixedSegmentSelector(int segmentWidth, int segmentHeight = 40, int segmentGap = 0, float segmentBorderWidth = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentWidth, 80);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentHeight, 32);
        _segmentGap = segmentGap;
        _segmentBorderWidth = segmentBorderWidth;
        _segmentWidth = segmentWidth;
        _segmentHeight = segmentHeight;
        AutoSize = false;
        Size = new Size(segmentWidth, segmentHeight);
        Margin = Padding.Empty;
        BackColor = UiPalette.Surface;
    }

    protected virtual float SegmentFontSize => 9.5F;

    public event EventHandler? SelectedValueChanged;

    public bool TryGetSelectedValue(out T value)
    {
        if (_nodes.Length == 0)
        {
            value = default!;
            return false;
        }

        value = _nodes[_selectedIndex].Value;
        return true;
    }

    public void SetNodes(IEnumerable<FixedChoiceNode<T>> nodes, T selectedValue)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        FixedChoiceNode<T>[] values = nodes.ToArray();
        ArgumentOutOfRangeException.ThrowIfZero(values.Length);
        if (!NodesMatch(values))
        {
            BuildSegments(values);
        }

        _nodes = values;
        int selectedIndex = Array.FindIndex(
            values,
            node => EqualityComparer<T>.Default.Equals(node.Value, selectedValue));
        _selectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        UpdateVisualState();
    }

    public bool SelectValue(T value)
    {
        int index = Array.FindIndex(
            _nodes,
            node => EqualityComparer<T>.Default.Equals(node.Value, value));
        if (index < 0)
        {
            return false;
        }

        _selectedIndex = index;
        UpdateVisualState();
        return true;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        UpdateVisualState();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutSegments();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeButtons();
        }

        base.Dispose(disposing);
    }

    private void BuildSegments(FixedChoiceNode<T>[] nodes)
    {
        SuspendLayout();
        try
        {
            DisposeButtons();
            Controls.Clear();
            _buttons = new ModernStepButton[nodes.Length];
            for (int index = 0; index < nodes.Length; index++)
            {
                ModernStepButton button = new()
                {
                    AutoSize = false,
                    OutlineWidth = _segmentBorderWidth,
                    Font = new Font("Microsoft YaHei UI", SegmentFontSize, FontStyle.Regular, GraphicsUnit.Point),
                    Tag = index,
                    Text = nodes[index].DisplayText,
                };
                button.Click += OnSegmentClicked;
                _buttons[index] = button;
                Controls.Add(button);
            }

            LayoutSegments();
        }
        finally
        {
            ResumeLayout(performLayout: false);
        }
    }

    private void DisposeButtons()
    {
        foreach (Button button in _buttons)
        {
            button.Click -= OnSegmentClicked;
            button.Dispose();
        }

        _buttons = [];
    }

    private void LayoutSegments()
    {
        if (_buttons.Length == 0)
        {
            return;
        }

        int gap = UiLayoutMetrics.Pixels(this, _segmentGap);
        int baseWidth = (Width - gap * (_buttons.Length - 1)) / _buttons.Length;
        int usedWidth = 0;
        for (int index = 0; index < _buttons.Length; index++)
        {
            int width = index == _buttons.Length - 1
                ? Width - usedWidth
                : baseWidth;
            _buttons[index].Bounds = new Rectangle(usedWidth, 0, width, Height);
            usedWidth += width + gap;
        }
    }

    private bool NodesMatch(FixedChoiceNode<T>[] nodes)
    {
        if (_nodes.Length != nodes.Length)
        {
            return false;
        }

        for (int index = 0; index < nodes.Length; index++)
        {
            if (!EqualityComparer<T>.Default.Equals(_nodes[index].Value, nodes[index].Value) ||
                !string.Equals(
                    _nodes[index].DisplayText,
                    nodes[index].DisplayText,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void OnSegmentClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is not Button { Tag: int index } || index == _selectedIndex)
        {
            return;
        }

        _selectedIndex = index;
        UpdateVisualState();
        SelectedValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateVisualState()
    {
        for (int index = 0; index < _buttons.Length; index++)
        {
            ModernStepButton button = _buttons[index];
            bool selected = index == _selectedIndex;
            if (button.Selected != selected)
            {
                button.Selected = selected;
            }

            if (button.Enabled != Enabled)
            {
                button.Enabled = Enabled;
            }
        }
    }
}
