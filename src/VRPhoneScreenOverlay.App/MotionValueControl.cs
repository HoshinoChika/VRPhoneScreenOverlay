using System.ComponentModel;
using System.Globalization;

namespace VRPhoneScreenOverlay.App;

[ToolboxItem(true)]
public sealed class MotionValueControl : UserControl
{
    private readonly RepeatingStepButton _minus = new() { Text = "−", AccessibleName = "减小" };
    private readonly RepeatingStepButton _plus = new() { Text = "+", AccessibleName = "增大" };
    private readonly TextBox _editor = new() { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center };
    private readonly SurfacePanel _box = new() { SurfaceColor = UiPalette.Window, BorderColor = UiPalette.Border };
    private decimal _value;
    private decimal _minimum;
    private decimal _maximum = 100;

    public MotionValueControl()
    {
        AutoScaleMode = AutoScaleMode.None;
        Size = new Size(180, 36);
        BackColor = UiPalette.SurfaceRaised;
        _editor.BackColor = UiPalette.Window;
        _editor.ForeColor = UiPalette.TextPrimary;
        _box.Controls.Add(_editor);
        Controls.Add(_minus);
        Controls.Add(_box);
        Controls.Add(_plus);
        _minus.Step += count => Adjust(-count);
        _plus.Step += Adjust;
        _editor.Validated += (_, _) => Commit();
        _editor.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
            else if (e.KeyCode is Keys.Up or Keys.Down) { Adjust(e.KeyCode == Keys.Up ? 1 : -1); e.SuppressKeyPress = true; }
        };
        RenderValue();
        Arrange();
    }

    public event EventHandler? ValueChanged;
    [DefaultValue(typeof(decimal), "0")]
    public decimal Minimum { get => _minimum; set { _minimum = value; SetValue(_value, false); } }
    [DefaultValue(typeof(decimal), "100")]
    public decimal Maximum { get => _maximum; set { _maximum = value; SetValue(_value, false); } }
    [DefaultValue(typeof(decimal), "0.1")]
    public decimal Increment { get; set; } = 0.1m;
    [DefaultValue(2)]
    public int DecimalPlaces { get; set; } = 2;
    [DefaultValue(typeof(decimal), "0")]
    public decimal Value { get => _value; set => SetValue(value, true); }
    internal void SelectValue(decimal value) { if (value != _value) { SetValue(value, false); } }
    internal void Commit()
    {
        if (decimal.TryParse(_editor.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal value)) { SetValue(value, true); }
        else { RenderValue(); }
    }
    internal void Adjust(int steps) { Commit(); SetValue(_value + Increment * steps, true); }

    private void SetValue(decimal value, bool notify)
    {
        decimal next = Math.Round(Math.Clamp(value, _minimum, Math.Max(_minimum, _maximum)), DecimalPlaces);
        bool changed = _value != next;
        _value = next;
        RenderValue();
        if (changed && notify) { ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    private void RenderValue()
    {
        _editor.Text = _value.ToString("F" + DecimalPlaces, CultureInfo.CurrentCulture);
        _minus.Enabled = _value > _minimum;
        _plus.Enabled = _value < _maximum;
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Arrange(); }
    private void Arrange()
    {
        if (_box is null || _editor is null) { return; }
        int button = Height;
        int gap = Math.Max(3, Height / 6);
        _minus.SetBounds(0, 0, button, Height);
        _plus.SetBounds(Width - button, 0, button, Height);
        _box.SetBounds(button + gap, 0, Math.Max(30, Width - 2 * (button + gap)), Height);
        _editor.SetBounds(gap, Math.Max(0, (Height - _editor.PreferredHeight) / 2), Math.Max(20, _box.Width - 2 * gap), _editor.PreferredHeight);
    }
}

internal sealed class RepeatingStepButton : ModernStepButton
{
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly HoldRepeatState _hold = new();
    private bool _suppressReleaseClick;
    internal event Action<int>? Step;
    internal static int RepeatUnits(long heldMilliseconds) => heldMilliseconds >= 2000 ? 5 : 1;
    public RepeatingStepButton() => _timer.Tick += OnRepeat;
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) { return; }
        _suppressReleaseClick = true;
        _hold.Start(Environment.TickCount64);
        Capture = true;
        Step?.Invoke(1);
        if (!Enabled) { return; }
        _timer.Interval = 400;
        _timer.Start();
    }
    private void StopHold() { _timer.Stop(); _hold.Stop(); }
    private void OnRepeat(object? sender, EventArgs e)
    {
        if (!Enabled || !Visible || !Capture || FindForm() is { ContainsFocus: false }) { StopHold(); return; }
        _timer.Interval = 60;
        int count = _hold.Poll(Environment.TickCount64);
        if (count > 0) { Step?.Invoke(count); }
    }
    protected override void OnClick(EventArgs e) { if (!_suppressReleaseClick) { Step?.Invoke(1); base.OnClick(e); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); StopHold(); _suppressReleaseClick = false; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { StopHold(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); StopHold(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); StopHold(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) { _timer?.Stop(); _hold?.Stop(); _suppressReleaseClick = false; } }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) { _timer?.Stop(); _hold?.Stop(); _suppressReleaseClick = false; } }
    protected override void OnKeyDown(KeyEventArgs e) { _suppressReleaseClick = false; base.OnKeyDown(e); }
    protected override void Dispose(bool disposing) { if (disposing) { _timer.Dispose(); } base.Dispose(disposing); }
}

internal sealed class HoldRepeatState
{
    private bool _active;
    private long _start;
    private long _next;
    public void Start(long now) { _active = true; _start = now; _next = now + 400; }
    public void Stop() => _active = false;
    public int Poll(long now)
    {
        if (!_active || now < _next) { return 0; }
        _next = now + 60;
        return RepeatingStepButton.RepeatUnits(now - _start);
    }
}
