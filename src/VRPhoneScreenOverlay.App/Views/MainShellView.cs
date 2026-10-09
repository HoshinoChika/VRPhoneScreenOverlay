using System.Drawing.Drawing2D;

namespace VRPhoneScreenOverlay.App.Views;

public partial class MainShellView : UserControl
{
    private bool _confirmingClose;
    private bool _pageWasEnabled;
    private bool _sidebarWasEnabled;

    public MainShellView()
    {
        InitializeComponent();
        titleDivider.BringToFront();
        closeConfirmation.VisibleChanged += (_, _) =>
        {
            if (closeConfirmation.Visible) { contextHelp.HideHelp(); }
            if (closeConfirmation.Visible && !_confirmingClose)
            {
                _pageWasEnabled = pageHost.Enabled;
                _sidebarWasEnabled = sidebar.Enabled;
                _confirmingClose = true;
                pageHost.Enabled = false;
                sidebar.Enabled = false;
            }
            else if (!closeConfirmation.Visible && _confirmingClose)
            {
                _confirmingClose = false;
                pageHost.Enabled = _pageWasEnabled;
                sidebar.Enabled = _sidebarWasEnabled;
            }
        };
    }

    internal ContextHelpPopup ContextHelp => contextHelp;
    internal void ShowContextHelp(Control owner, string message)
    {
        if (!closeConfirmation.Visible) { contextHelp.ShowFor(owner, message, ContentScale); }
    }
    internal void HideContextHelp(Control owner)
    { if (ReferenceEquals(contextHelp.Owner, owner)) { contextHelp.HideHelp(); } }
    protected override void OnSizeChanged(EventArgs e)
    { contextHelp?.HideHelp(); base.OnSizeChanged(e); }

    internal Panel PageHost => pageHost;
    internal void ShowMainPage(Control page, NavigationButton selectedNavigation)
    {
        contextHelp.HideHelp();
        // Reset the destination before showing it, avoiding a transient QR restart.
        if (page is HomePageView home) { home.ShowMainContent(); }
        else if (page is AboutPageView about) { about.ShowMainContent(); }
        foreach (Control sibling in pageHost.Controls) { sibling.Visible = ReferenceEquals(sibling, page); }
        foreach (NavigationButton button in new[] { homeNavigation, featuresNavigation, videoNavigation, settingsNavigation, aboutNavigation })
        { button.Selected = ReferenceEquals(button, selectedNavigation); }
        page.Visible = true;
        page.BringToFront();
    }
    internal CloseConfirmationView CloseConfirmation => closeConfirmation;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal float ContentScale { get; set; } = 1f;

    internal NavigationButton HomeNavigation => homeNavigation;

    internal NavigationButton SettingsNavigation => settingsNavigation;

    internal NavigationButton FeaturesNavigation => featuresNavigation;

    internal NavigationButton VideoNavigation => videoNavigation;

    internal NavigationButton AboutNavigation => aboutNavigation;

    internal WindowControlButton MinimizeButton => minimizeButton;

    internal WindowControlButton CloseButton => closeButton;

    internal IEnumerable<Control> TitleBarDragSurfaces =>
        [titleBar, appMark, productTitle];

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(
            1,
            1,
            Math.Max(0, ClientSize.Width - 3),
            Math.Max(0, ClientSize.Height - 3));
        using GraphicsPath frame = SurfacePanel.CreateRoundedRectangle(bounds, 13);
        using Pen border = new(UiPalette.WindowBorder);
        e.Graphics.DrawPath(border, frame);
    }
}
