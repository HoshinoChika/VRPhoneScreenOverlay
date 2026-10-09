using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private readonly List<Control> _windowDragSurfaces = [];
    private Control? _activeDragSurface;
    private Point _dragStartCursor;
    private Point _dragStartWindow;

    private void InitializeVisualTree(bool connectionLogEnabled)
    {
        Text = AppIdentity.ProductName;
        _windowIcon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (_windowIcon is not null)
        {
            Icon = _windowIcon;
        }

        ConfigureTray(new WindowTray(Icon ?? SystemIcons.Application, AppIdentity.ProductName));

        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = false;
        AutoScroll = false;
        ClientSize = UiLayoutMetrics.MainWindowClientSize;
        FormBorderStyle = FormBorderStyle.None;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        SizeGripStyle = SizeGripStyle.Hide;
        DoubleBuffered = true;
        BackColor = UiPalette.Window;
        ForeColor = UiPalette.TextPrimary;
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);

        MainShellView shell = new()
        {
            Location = Point.Empty,
            Size = ClientSize,
        };
        HomePageView homePage = new() { Location = UiLayoutMetrics.PageLocation };
        SettingsPageView settingsPage = new() { Location = UiLayoutMetrics.PageLocation };
        ControllerBindingsView featuresPage = new() { Location = UiLayoutMetrics.PageLocation };
        VideoPageView videoPage = new() { Location = UiLayoutMetrics.PageLocation };
        AboutPageView aboutPage = new() { Location = UiLayoutMetrics.PageLocation };

        _motionView = homePage.MotionPanel;
        _inertiaToggle = homePage.InertiaToggle;
        _inertiaToggle.CheckedChanged += OnMotionParametersChanged;
        _motionView.ParametersChanged += OnMotionParametersChanged;
        _wirelessView = homePage.WirelessPanel;
        _wirelessView.VisibleChanged += (_, _) =>
        {
            if (!_wirelessView.Visible) { _wirelessOperation?.Cancel(); _wirelessView.ShowQr(null); }
            else { TryStartAutomaticQr(); }
        };
        _wirelessView.DeviceManagerHost.VisibleChanged += (_, _) =>
        {
            if (!_wirelessView.DeviceManagerHost.Visible) { _qrPausedForDevice = false; TryStartAutomaticQr(); }
        };
        _wirelessView.MethodChanged += OnConnectionMethodChanged;
        _wirelessView.AutoPairButton.Click += OnWirelessOperationClicked;
        _wirelessView.PairButton.Click += OnWirelessOperationClicked;
        _wirelessView.ConnectButton.Click += OnWirelessOperationClicked;
        _wirelessView.CancelButton.Click += (_, _) => _wirelessOperation?.Cancel();
        _wirelessView.DeviceManager.SaveButton.Click += OnManagedDeviceSave;
        _wirelessView.DeviceManager.ConnectButton.Click += OnManagedDeviceConnect;
        _wirelessView.DeviceManager.ForgetButton.Click += OnManagedDeviceForget;
        _phoneStatus = homePage.PhoneStatus;
        _phoneName = homePage.PhoneName;
        _phoneDetails = homePage.PhoneDetails;
        _adbDetail = homePage.AdbDetail;
        _videoStatus = homePage.VideoStatus;
        _audioStatus = homePage.AudioStatus;
        _controlStatus = homePage.ControlStatus;
        _steamVrStatus = homePage.SteamVrStatus;
        _phoneOverlayButton = homePage.OpenOverlayButton;
        _metricResolution = homePage.MetricResolution;
        _metricBitrate = homePage.MetricBitrate;
        _metricFrameRate = homePage.MetricFrameRate;
        _metricLatency = homePage.MetricLatency;
        _playspaceStatus = homePage.PlayspaceStatus;
        _playspaceCoordinates = homePage.PlayspaceCoordinates;
        _playspaceDragToggle = homePage.PlayspaceToggle;
        _deviceList = homePage.DeviceList;
        _historyDeviceList = _wirelessView.HistoryDeviceList;
        _refreshButton = homePage.RefreshButton;
        _videoProbeButton = homePage.VideoProbeButton;
        _operationStatus = homePage.OperationStatus;
        _bindingGuideView = featuresPage;
        _bindingGuideView.ControllerSelector.SetNodes(ControllerBindingGuideService.Choices.Select(
            choice => new FixedChoiceNode<string>(choice.Key, choice.Label)), "pico_controller");
        _bindingGuideView.RefreshButton.Click += OnBindingGuideRequested;
        _bindingGuideView.RestoreBindingsButton.Click += OnRestoreDefaultBindingsClicked;
        _bindingGuideView.ControllerSelector.SelectedValueChanged += OnBindingGuideRequested;

        _controllerHandSelector = featuresPage.HandSelector;
        _resolutionSelector = videoPage.ResolutionSelector;
        _bitrateSelector = videoPage.BitrateSelector;
        _frameRateSelector = videoPage.FrameRateSelector;
        _steamVrBindingsButton = featuresPage.OpenBindingsButton;
        _keepAwakeWhileGrabbedToggle = settingsPage.KeepAwakeWhileGrabbedToggle;
        _vrUnlockKeypadToggle = settingsPage.VrUnlockKeypadToggle;
        _settingsView = settingsPage;
        _discardSettingsButton = videoPage.DiscardButton;
        _applySettingsButton = videoPage.ApplyButton;
        _qualityStatus = videoPage.QualityStatus;
        _steamVrBindingNotice = featuresPage.BindingNotice;
        _videoOperationStatus = videoPage.Status;

        _updateChannelSelector = aboutPage.UpdateChannelSelector;
        _checkUpdateButton = aboutPage.CheckUpdateButton;
        _aboutPage = aboutPage;
        _prepareDiagnosticsButton = aboutPage.UploadDiagnosticsButton;
        _uploadDiagnosticsButton = aboutPage.ConfirmDiagnosticUploadButton;
        _cancelDiagnosticReportButton = aboutPage.CancelDiagnosticReportButton;
        _diagnosticIssueTypeComboBox = aboutPage.IssueTypeComboBox;
        _diagnosticOccurredAtPicker = aboutPage.OccurredAtPicker;
        _diagnosticDescriptionTextBox = aboutPage.IssueDescriptionTextBox;
        _aboutOperationStatus = aboutPage.OperationStatus;
        aboutPage.SetProductDetails($"版本 {DisplayVersion}");

        _runtimeStatus = new Label
        {
            AutoSize = false,
            Text = FormatRuntimeStatus(),
            Visible = false,
        };
        shell.Controls.Add(_runtimeStatus);

        _controllerHandSelector.SetNodes(
        [
            new(ControllerHandPreference.Left, "左手"),
            new(ControllerHandPreference.Right, "右手"),
        ],
        _pendingSettings.ControllerHand);
        _bitrateSelector.SetNodes(
            AppSettingsPolicy.VideoBitratesMbps.Order().Select(
                value => new FixedChoiceNode<int>(value, $"{value} Mbps")),
            _pendingSettings.VideoBitrateMbps);
        _frameRateSelector.SetNodes(
            AppSettingsPolicy.VideoMaximumFrameRates.Order().Select(
                value => new FixedChoiceNode<int>(value, $"{value} FPS")),
            _pendingSettings.VideoMaximumFramesPerSecond);
        _updateChannelSelector.SetNodes(
        [
            new(UpdateChannel.Beta, "测试"),
        ],
        UpdateChannel.Beta);
        _updateChannelSelector.Enabled = false;
        _diagnosticIssueTypeComboBox.SetItems(
            _diagnosticIssueTypes.Select(item => item.Label));

        shell.PageHost.Controls.Add(aboutPage);
        shell.PageHost.Controls.Add(featuresPage);
        shell.PageHost.Controls.Add(videoPage);
        shell.PageHost.Controls.Add(settingsPage);
        shell.PageHost.Controls.Add(homePage);
        shell.HomeNavigation.Click += (_, _) => shell.ShowMainPage(homePage, shell.HomeNavigation);
        shell.SettingsNavigation.Click += (_, _) => shell.ShowMainPage(settingsPage, shell.SettingsNavigation);
        shell.FeaturesNavigation.Click += (_, _) => shell.ShowMainPage(featuresPage, shell.FeaturesNavigation);
        shell.FeaturesNavigation.Click += OnBindingGuideRequested;
        shell.VideoNavigation.Click += (_, _) => shell.ShowMainPage(videoPage, shell.VideoNavigation);
        shell.AboutNavigation.Click += (_, _) => shell.ShowMainPage(aboutPage, shell.AboutNavigation);
        shell.MinimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        shell.CloseButton.Click += (_, _) => Close();
        _closeConfirmation = shell.CloseConfirmation;
        _closeConfirmation.MinimizeButton.Click += OnConfirmationPrimaryClicked;
        _closeConfirmation.CancelButton.Click += (_, _) => _closeConfirmation.Visible = false;
        _closeConfirmation.ExitButton.Click += OnConfirmationSecondaryClicked;
        _windowDragSurfaces.AddRange(shell.TitleBarDragSurfaces);
        foreach (Control dragSurface in _windowDragSurfaces)
        {
            dragSurface.MouseDown += OnTitleBarMouseDown;
            dragSurface.MouseMove += OnTitleBarMouseMove;
            dragSurface.MouseUp += OnTitleBarMouseUp;
        }

        Controls.Add(shell);
        shell.ShowMainPage(homePage, shell.HomeNavigation);
        _displayLayout = new UiScaleLayout(shell);
    }

    private static string DisplayVersion => Application.ProductVersion.Split('+', 2)[0];

    private void OnTitleBarMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _activeDragSurface = sender as Control;
        _dragStartCursor = Cursor.Position;
        _dragStartWindow = Location;
        if (_activeDragSurface is not null)
        {
            _activeDragSurface.Capture = true;
        }
    }

    private void OnTitleBarMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (_activeDragSurface is null || (eventArgs.Button & MouseButtons.Left) == 0)
        {
            return;
        }

        Point cursor = Cursor.Position;
        Location = new Point(
            _dragStartWindow.X + cursor.X - _dragStartCursor.X,
            _dragStartWindow.Y + cursor.Y - _dragStartCursor.Y);
    }

    private void OnTitleBarMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left || _activeDragSurface is null)
        {
            return;
        }

        _activeDragSurface.Capture = false;
        _activeDragSurface = null;
    }
}
