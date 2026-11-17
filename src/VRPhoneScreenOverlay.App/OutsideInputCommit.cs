namespace VRPhoneScreenOverlay.App;

/// <summary>Commits a visible editor on clicks outside it and when its window loses activation.</summary>
internal sealed class OutsideInputCommit : IDisposable
{
    private readonly Form _form;
    private readonly Control _input;
    private readonly Action _commit;
    private readonly HashSet<Control> _controls = [];

    internal OutsideInputCommit(Form form, MotionValueControl input) : this(form, input, input.Commit) { }

    internal OutsideInputCommit(Form form, Control input, Action commit)
    {
        _form = form;
        _input = input;
        _commit = commit;
        Attach(form);
        _form.Deactivate += OnDeactivate;
    }

    private void Attach(Control control)
    {
        if (!_controls.Add(control)) { return; }
        control.MouseDown += OnMouseDown;
        control.ControlAdded += OnControlAdded;
        control.ControlRemoved += OnControlRemoved;
        foreach (Control child in control.Controls) { Attach(child); }
    }

    private void Detach(Control control)
    {
        if (!_controls.Remove(control)) { return; }
        control.MouseDown -= OnMouseDown;
        control.ControlAdded -= OnControlAdded;
        control.ControlRemoved -= OnControlRemoved;
        foreach (Control child in control.Controls) { Detach(child); }
    }

    private void OnControlAdded(object? sender, ControlEventArgs e) { if (e.Control is { } control) { Attach(control); } }
    private void OnControlRemoved(object? sender, ControlEventArgs e) { if (e.Control is { } control) { Detach(control); } }
    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        for (Control? target = sender as Control; target is not null; target = target.Parent)
        { if (ReferenceEquals(target, _input)) { return; } }
        Commit();
    }
    private void OnDeactivate(object? sender, EventArgs e) => Commit();
    private void Commit()
    {
        if (!_input.IsDisposed && _input.Visible && _input.Enabled) { _commit(); }
    }
    public void Dispose()
    {
        _form.Deactivate -= OnDeactivate;
        foreach (Control control in _controls.ToArray()) { Detach(control); }
    }
}
