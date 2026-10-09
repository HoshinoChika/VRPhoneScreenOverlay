using Microsoft.Win32;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private UiScaleLayout? _displayLayout;
    private bool _applyingDisplayScale;
    private float _displayScale;
    private bool _displayEventsAttached;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyDisplayScale(center: true);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _displayEventsAttached = true;
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (Visible && !_applyingDisplayScale) { ApplyDisplayScale(center: false); }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Our fixed layout uses both screen size and DPI. Do not let the default
        // suggested rectangle resize it independently of the content.
        e.Cancel = true;
        base.OnDpiChanged(e);
        QueueDisplayRefresh();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => QueueDisplayRefresh();

    private void QueueDisplayRefresh()
    {
        try
        {
            if (IsHandleCreated && !IsDisposed && !Disposing)
            {
                BeginInvoke(() => ApplyDisplayScale(center: false, force: true));
            }
        }
        catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated)
        {
            // Display notifications may race the window handle's teardown.
        }
    }

    private void ApplyDisplayScale(bool center, bool force = false)
    {
        if (_displayLayout is null || _applyingDisplayScale || IsDisposed || Disposing) { return; }
        Screen screen = Screen.FromControl(this);
        Rectangle work = screen.WorkingArea;
        float scale = UiLayoutMetrics.DisplayScale(screen.Bounds.Size, work.Size, DeviceDpi);
        if (!force && MathF.Abs(scale - _displayScale) < 0.001f) { return; }
        _applyingDisplayScale = true;
        SuspendLayout();
        try
        {
            _displayLayout.Apply(scale);
            UiLayoutMetrics.LockWindowSize(this, scale);
            _displayScale = scale;
            Location = center
                ? new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2)
                : new Point(Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width)),
                    Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height)));
            // On older Windows without DWM corners, rebuild the managed clip
            // after scaling so the old 760x502 region cannot clip the new window.
            if (Region is not null)
            {
                using System.Drawing.Drawing2D.GraphicsPath path = SurfacePanel.CreateRoundedRectangle(
                    new Rectangle(0, 0, Width - 1, Height - 1), (int)MathF.Round(14 * scale));
                Region previous = Region;
                Region = new Region(path);
                previous.Dispose();
            }
        }
        finally
        {
            ResumeLayout(true);
            _applyingDisplayScale = false;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DetachDisplayEvents();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { DetachDisplayEvents(); }
        base.Dispose(disposing);
    }

    private void DetachDisplayEvents()
    {
        if (_displayEventsAttached)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _displayEventsAttached = false;
        }
    }
}
