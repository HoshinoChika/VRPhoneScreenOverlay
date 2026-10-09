using System.Drawing.Drawing2D;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Core;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Presentation;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm : ExitConfirmationForm
{
    protected override void ShowExitConfirmation()
    {
        if (_installingUpdate) { _closeConfirmation.BringToFront(); return; }
        _closeConfirmation.ConfigureClose();
        _closeConfirmation.Visible = true;
        _closeConfirmation.BringToFront();
        _closeConfirmation.MinimizeButton.Focus();
    }

    private enum PhoneOverlayButtonProgress
    {
        None,
        Opening,
        Closing,
    }

    private static readonly int[] _playspaceDragMultipliers = [1, 5, 10, 20, 40];
    private static readonly (DiagnosticIssueType Type, string Label)[] _diagnosticIssueTypes =
    [
        (DiagnosticIssueType.Video, "视频异常"),
        (DiagnosticIssueType.Audio, "音频异常"),
        (DiagnosticIssueType.Control, "控制异常"),
        (DiagnosticIssueType.DeviceConnection, "手机连接异常"),
        (DiagnosticIssueType.SteamVrBinding, "SteamVR / 手柄绑定异常"),
        (DiagnosticIssueType.PlayspaceDrag, "空间拖拽异常"),
        (DiagnosticIssueType.Update, "软件更新异常"),
        (DiagnosticIssueType.Other, "其他问题"),
    ];
    private readonly AppRuntime _runtime;
    private readonly ISharingSession _sharing;
    private readonly IAndroidConnectionService _phone;
    private readonly IPhoneVideoDecodeProbeService _videoProbe;
    private readonly IPhoneOverlayService _phoneOverlay;
    private readonly IPhoneAudioService _phoneAudio;
    private readonly IPhoneControlService _phoneControl;
    private readonly IPhoneMediaSessionCoordinator _mediaSession;
    private readonly IOpenVrPlayspaceDragService _playspaceDrag;
    private readonly IAppSettingsService _settings;
    private readonly SettingsApplicationService _settingsApplication;
    private readonly IUpdateService _updateService;
    private readonly IAnonymousDiagnosticsService _diagnosticsService;
    private readonly string? _connectionLogPath;
    private Label _runtimeStatus = null!;
    private Label _phoneStatus = null!;
    private Label _phoneDetails = null!;
    private Label _adbDetail = null!;
    private Label _videoStatus = null!;
    private Label _audioStatus = null!;
    private Label _controlStatus = null!;
    private Label _steamVrStatus = null!;
    private Label _operationStatus = null!;
    private Label _metricResolution = null!;
    private Label _metricBitrate = null!;
    private Label _metricFrameRate = null!;
    private Label _metricLatency = null!;
    private Label _playspaceStatus = null!;
    private Label _playspaceCoordinates = null!;
    private ListBox _deviceList = null!;
    private Button _refreshButton = null!;
    private Button _videoProbeButton = null!;
    private ModernButton _phoneOverlayButton = null!;
    private Button _steamVrBindingsButton = null!;
    private Button _checkUpdateButton = null!;
    private Button _uploadDiagnosticsButton = null!;
    private Button _prepareDiagnosticsButton = null!;
    private Button _cancelDiagnosticReportButton = null!;
    private DiagnosticIssueSelectorControl _diagnosticIssueTypeComboBox = null!;
    private ModernDateTimeInput _diagnosticOccurredAtPicker = null!;
    private ModernMultilineInput _diagnosticDescriptionTextBox = null!;
    private AboutPageView _aboutPage = null!;
    private Label _aboutOperationStatus = null!;
    private CheckBox _keepAwakeWhileGrabbedToggle = null!;
    private CheckBox _vrUnlockKeypadToggle = null!;
    private SettingsPageView _settingsView = null!;
    private Label _videoOperationStatus = null!;
    private ControllerBindingsView _bindingGuideView = null!;
    private int _bindingGuideRevision;
    private Label _phoneName = null!;
    private CloseConfirmationView _closeConfirmation = null!;
    private readonly OverlayWindowPolicy _windowPolicy = new();
    private FixedSegmentSelector<ControllerHandPreference> _controllerHandSelector = null!;
    private readonly PhoneBindingDefaultsService _bindingDefaults;
    private readonly PhoneBindingSynchronizationService? _bindingSynchronization;
    private OpenVrBindingResult? _startupSteamVrWarning;
    private CheckBox _playspaceDragToggle = null!;
    private FixedStepSelector<VideoResolutionProfile> _resolutionSelector = null!;
    private FixedStepSelector<int> _bitrateSelector = null!;
    private FixedStepSelector<int> _frameRateSelector = null!;
    private FixedSegmentSelector<UpdateChannel> _updateChannelSelector = null!;
    private Button _applySettingsButton = null!;
    private Button _discardSettingsButton = null!;
    private Label _qualityStatus = null!;
    private SteamVrBindingNotice _steamVrBindingNotice = null!;
    private readonly CancellationTokenSource _formLifetime = new();
    private readonly UiOperationLifetime _uiOperations = new();
    private DateTimeOffset _nextPhoneControlStartAt;
    private bool _updatingDeviceList;
    private bool _operationInProgress;
    private bool _immediateSettingsInProgress;
    private bool _bindingRecoveryInProgress;
    private bool _updatingSettingsControls;
    private AppSettings _pendingSettings;
    private bool _settingsDirty;
    private bool _aboutOperationInProgress;
    private PhoneOverlayButtonProgress _phoneOverlayButtonProgress;
    private UpdateRelease? _availableUpdate;
    private Icon? _windowIcon;

    public MainForm(
        AppRuntime runtime,
        ISharingSession sharing,
        IAndroidConnectionService phone,
        IPhoneVideoDecodeProbeService videoProbe,
        IPhoneOverlayService phoneOverlay,
        IPhoneAudioService phoneAudio,
        IPhoneControlService phoneControl,
        IPhoneMediaSessionCoordinator mediaSession,
        IOpenVrPlayspaceDragService playspaceDrag,
        IAppSettingsService settings,
        IUpdateService updateService,
        IAnonymousDiagnosticsService diagnosticsService,
        string? connectionLogPath,
        Func<string>? picoStatus = null, SettingsApplicationService? settingsApplication = null,
        PhoneBindingSynchronizationService? bindingSynchronization = null, OpenVrBindingResult? startupSteamVrWarning = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _sharing = sharing ?? throw new ArgumentNullException(nameof(sharing));
        _phone = phone ?? throw new ArgumentNullException(nameof(phone));
        _videoProbe = videoProbe ?? throw new ArgumentNullException(nameof(videoProbe));
        _phoneOverlay = phoneOverlay ?? throw new ArgumentNullException(nameof(phoneOverlay));
        _phoneAudio = phoneAudio ?? throw new ArgumentNullException(nameof(phoneAudio));
        _phoneControl = phoneControl ?? throw new ArgumentNullException(nameof(phoneControl));
        _mediaSession = mediaSession ?? throw new ArgumentNullException(nameof(mediaSession));
        _playspaceDrag = playspaceDrag ?? throw new ArgumentNullException(nameof(playspaceDrag));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsApplication = settingsApplication ?? new SettingsApplicationService(settings, phoneOverlay, mediaSession, playspaceDrag, phone);
        _bindingDefaults = new();
        _bindingSynchronization = bindingSynchronization;
        _startupSteamVrWarning = startupSteamVrWarning is { Succeeded: false } ? startupSteamVrWarning : null;
        if (_bindingSynchronization is not null) { _bindingSynchronization.Changed += OnBindingSynchronizationChanged; }
        _connectionManagement = phone is IAndroidConnectionManagementService management
            ? new(phone, management, mediaSession, () => BuildVideoOptions(_settings.Snapshot.Value),
                () => _settings.Snapshot.Value.AutoOpenPhoneOverlay) : null;
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
        _connectionLogPath = connectionLogPath;
        _pendingSettings = _settings.Snapshot.Value;
        _motionWriter = new MotionSettingsWriter(_settings);
        _motionWriter.Saved += OnMotionSaved;
        _motionRuntime = MotionPreferences.From(_pendingSettings);
        _lastPersistedSettings = _pendingSettings;

        InitializeVisualTree(connectionLogPath is not null);
        _settingsView.PicoStatusProvider = picoStatus;
        ApplyMotionRuntime(_motionRuntime);

        _runtime.StateChanged += OnRuntimeStateChanged;
        _phone.StateChanged += OnPhoneStateChanged;
        _phoneOverlay.StateChanged += OnPhoneOverlayStateChanged;
        _phoneAudio.StateChanged += OnPhoneAudioStateChanged;
        _phoneControl.StateChanged += OnPhoneControlStateChanged;
        _mediaSession.StateChanged += OnPhoneMediaSessionStateChanged;
        _playspaceDrag.StateChanged += OnPlayspaceDragStateChanged;
        _settings.Changed += OnSettingsChanged;
        _deviceList.MouseClick += OnDeviceSelected;
        _deviceList.KeyDown += OnDeviceListKeyDown;
        _historyDeviceList.MouseClick += OnDeviceSelected;
        _historyDeviceList.KeyDown += OnDeviceListKeyDown;
        _refreshButton.Click += OnRefreshClicked;
        _videoProbeButton.Click += OnVideoProbeClicked;
        _phoneOverlayButton.Click += OnPhoneOverlayClicked;
        _steamVrBindingsButton.Click += OnSteamVrBindingsClicked;
        _checkUpdateButton.Click += OnCheckUpdateClicked;
        _prepareDiagnosticsButton.Click += OnPrepareDiagnosticsClicked;
        _uploadDiagnosticsButton.Click += OnUploadDiagnosticsClicked;
        _cancelDiagnosticReportButton.Click += OnCancelDiagnosticReportClicked;
        _diagnosticIssueTypeComboBox.SelectedValueChanged += OnDiagnosticIssueTypeChanged;
        _keepAwakeWhileGrabbedToggle.CheckedChanged += OnImmediateLockScreenSettingChanged;
        _vrUnlockKeypadToggle.CheckedChanged += OnImmediateLockScreenSettingChanged;
        _settingsView.AutoOpenToggle.CheckedChanged += OnAutomationSettingChanged;
        _settingsView.ScreenOffToggle.CheckedChanged += OnAutomationSettingChanged;
        _settingsView.MinimizeToggle.CheckedChanged += OnAutomationSettingChanged;
        _settingsView.PicoToggle.CheckedChanged += OnAutomationSettingChanged;
        _settingsView.SteamVrToggle.CheckedChanged += OnAutomationSettingChanged;
        _steamVrBindingNotice.ReloadRequested += OnReloadLocalBindingRequested;
        _controllerHandSelector.SelectedValueChanged += OnControllerHandChanged;
        _playspaceDragToggle.CheckedChanged += OnPlayspaceDragChanged;
        _resolutionSelector.SelectedValueChanged += OnSettingChoiceChanged;
        _bitrateSelector.SelectedValueChanged += OnSettingChoiceChanged;
        _frameRateSelector.SelectedValueChanged += OnSettingChoiceChanged;
        _updateChannelSelector.SelectedValueChanged += OnSettingChoiceChanged;
        _applySettingsButton.Click += OnApplySettingsClicked;
        _discardSettingsButton.Click += OnDiscardSettingsClicked;
        FormClosed += OnFormClosed;
        Shown += OnStartupUpdateShown;
        Shown += OnSteamVrStartupWarningShown;
        Shown += OnControllerDiscoveryShown;
        VisibleChanged += (_, _) => PresentPendingUpdate();
        _closeConfirmation.VisibleChanged += (_, _) =>
        {
            if (!_closeConfirmation.Visible && _pendingUpdatePrompt && IsHandleCreated && !IsDisposed)
            { BeginInvoke(PresentPendingUpdate); }
        };
        _phoneOverlay.ConfigureLockScreenFeatures(
            _pendingSettings.KeepAwakeWhileGrabbed,
            _pendingSettings.VrUnlockKeypadEnabled);
        RefreshSettingsControls();
        ConfigurePhoneAutomation();
        UpdatePhoneView(_phone.Snapshot);
        UpdateSteamVrBindingNotice(_phoneOverlay.Snapshot);
        UpdateDashboardSnapshots();
    }

    private async void OnCheckUpdateClicked(object? sender, EventArgs eventArgs)
    {
        _startupUpdateGate.Skip();
        await RunAboutOperationAsync(token => CheckForUpdatesAsync(false, token)).ConfigureAwait(true);
    }

    private async void OnUploadDiagnosticsClicked(object? sender, EventArgs eventArgs)
    {
        if (!TryCreateDiagnosticUserReport(out DiagnosticUserReport userReport))
        {
            return;
        }

        _aboutPage.HideDiagnosticReport();
        await RunAboutOperationAsync(async cancellationToken =>
        {
            AppSettings settings = _settings.Snapshot.Value;
            AndroidConnectionSnapshot android = _phone.Snapshot;
            PhoneMediaSessionSnapshot media = _mediaSession.Snapshot;
            PhoneOverlaySnapshot video = _phoneOverlay.Snapshot;
            PhoneOverlaySnapshot videoDiagnostics = _phoneOverlay.DiagnosticSnapshot;
            PhoneAudioSnapshot audio = _phoneAudio.Snapshot;
            PhoneAudioSnapshot audioDiagnostics = _phoneAudio.DiagnosticSnapshot;
            PhoneControlSnapshot control = _phoneControl.Snapshot;
            PhoneControlSnapshot controlDiagnostics = _phoneControl.DiagnosticSnapshot;
            OpenVrPlayspaceDragSnapshot playspace = _playspaceDrag.Snapshot;
            OpenVrBindingPreparationSnapshot bindingPreparation =
                OpenVrBindingRecovery.DiagnosticSnapshot;
            DiagnosticUploadContext context = DiagnosticContextBuilder.Build(new(
                DisplayVersion, _runtime.Snapshot, settings, android, media, video, videoDiagnostics,
                audio, audioDiagnostics, control, controlDiagnostics, playspace, bindingPreparation,
                new(_lastUpdateCheckAt, _lastUpdateReason, _lastUpdateFailureKind, _lastUpdateHttpStatus),
                userReport, _connectionLogPath));
            Progress<DiagnosticProgress> progress = new(value =>
            {
                string text = value.TotalBytes > 0 && value.CompletedBytes > 0
                    ? $"{value.Phase} {value.CompletedBytes * 100 / value.TotalBytes}%"
                    : value.Phase;
                SetControlText(_aboutPage.DiagnosticsStatus, text);
            });
            DiagnosticUploadResult result = await _diagnosticsService.BuildAndUploadAsync(
                context,
                progress,
                cancellationToken);
            SetControlText(_aboutPage.DiagnosticsStatus, result.Message);
            SetControlForeColor(
                _aboutPage.DiagnosticsStatus,
                result.Succeeded ? UiPalette.Success : UiPalette.Danger);
        }, _aboutPage.DiagnosticsStatus).ConfigureAwait(true);
    }

    private void OnPrepareDiagnosticsClicked(object? sender, EventArgs eventArgs)
    {
        if (_aboutOperationInProgress)
        {
            return;
        }

        _aboutPage.ShowDiagnosticReport();
        UpdateDiagnosticUploadButton();
        _diagnosticIssueTypeComboBox.Focus();
    }

    private void OnCancelDiagnosticReportClicked(object? sender, EventArgs eventArgs)
    {
        _aboutPage.HideDiagnosticReport();
        SetControlText(_aboutPage.DiagnosticsStatus, "已取消上传诊断包");
        SetControlForeColor(_aboutPage.DiagnosticsStatus, UiPalette.TextSecondary);
    }

    private void OnDiagnosticIssueTypeChanged(object? sender, EventArgs eventArgs) =>
        UpdateDiagnosticUploadButton();

    private void UpdateDiagnosticUploadButton()
    {
        _uploadDiagnosticsButton.Enabled = !_aboutOperationInProgress &&
            _diagnosticIssueTypeComboBox.SelectedIndex >= 0;
    }

    private bool TryCreateDiagnosticUserReport(out DiagnosticUserReport report)
    {
        int selectedIndex = _diagnosticIssueTypeComboBox.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _diagnosticIssueTypes.Length)
        {
            SetControlText(_aboutPage.DiagnosticsStatus, "请先选择问题类型");
            SetControlForeColor(_aboutPage.DiagnosticsStatus, UiPalette.Warning);
            report = null!;
            return false;
        }

        DateTime localTime = DateTime.SpecifyKind(
            _diagnosticOccurredAtPicker.Value,
            DateTimeKind.Unspecified);
        DateTimeOffset occurredAt = new(localTime, TimeZoneInfo.Local.GetUtcOffset(localTime));
        if (!UiInputPolicy.TryNormalizeDescription(
                _diagnosticDescriptionTextBox.Text,
                out string? description))
        {
            SetControlText(_aboutPage.DiagnosticsStatus, "现象描述不能超过 300 字");
            SetControlForeColor(_aboutPage.DiagnosticsStatus, UiPalette.Warning);
            report = null!;
            return false;
        }

        report = new DiagnosticUserReport(
            _diagnosticIssueTypes[selectedIndex].Type,
            occurredAt,
            description);
        return true;
    }

    private async Task RunAboutOperationAsync(Func<CancellationToken, Task> operation, Label? statusTarget = null)
    {
        if (_aboutOperationInProgress)
        {
            return;
        }

        _aboutOperationInProgress = true;
        _checkUpdateButton.Enabled = false;
        _prepareDiagnosticsButton.Enabled = false;
        _uploadDiagnosticsButton.Enabled = false;
        try
        {
            string? error = await AboutOperationRunner.RunAsync(operation, _formLifetime.Token);
            if (error is not null && !IsDisposed && !Disposing)
            {
                SetControlText(statusTarget ?? _aboutOperationStatus, error);
                if (_closeConfirmation.IsUpdate) { _closeConfirmation.Description.Text = error; }
                SetControlForeColor(statusTarget ?? _aboutOperationStatus, UiPalette.Danger);
            }
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _aboutOperationInProgress = false;
                _checkUpdateButton.Enabled = true;
                _prepareDiagnosticsButton.Enabled = true;
                UpdateDiagnosticUploadButton();
            }
        }
    }

    private static Button CreateButton(string text, Rectangle bounds)
    {
        return new Button
        {
            AutoSize = false,
            Bounds = bounds,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiPalette.LegacyButton,
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
        };
    }

    private Label CreateFixedLabel(
        string text,
        Rectangle bounds,
        Color foreColor,
        ContentAlignment textAlign = ContentAlignment.TopLeft)
    {
        return new Label
        {
            AutoSize = false,
            Bounds = bounds,
            BackColor = BackColor,
            ForeColor = foreColor,
            Text = text,
            TextAlign = textAlign,
            UseMnemonic = false,
        };
    }

    private Label CreateStatusDisplay(
        string text,
        Rectangle bounds,
        Color foreColor,
        FontStyle fontStyle = FontStyle.Regular)
    {
        return new Label
        {
            AutoSize = false,
            Bounds = bounds,
            BackColor = UiPalette.LegacyListBackground,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(Font, fontStyle),
            ForeColor = foreColor,
            Padding = new Padding(8, 5, 8, 5),
            Text = text,
            UseMnemonic = false,
        };
    }

    private int MeasureFixedValueWidth(string longestText)
    {
        Size measured = TextRenderer.MeasureText(
            longestText,
            Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return Math.Max(80, measured.Width + 24);
    }

    private string FormatRuntimeStatus() =>
        $"运行 {_runtime.Snapshot.State} · {_runtime.Snapshot.Reason.Code}";

    private void OnRuntimeStateChanged(object? sender, AppRuntimeStateChangedEventArgs eventArgs)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnRuntimeStateChanged(sender, eventArgs));
            return;
        }

        SetControlText(_runtimeStatus, FormatRuntimeStatus());
    }

    private void OnPhoneStateChanged(object? sender, AndroidConnectionChangedEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnPhoneStateChanged(sender, eventArgs));
            return;
        }

        UpdatePhoneView(eventArgs.Snapshot);
        ConfigurePhoneAutomation();
    }

    private void UpdatePhoneView(AndroidConnectionSnapshot snapshot)
    {
        SetControlText(_phoneStatus, ConnectionStateName(snapshot.State));
        string? phoneName = snapshot.Devices.FirstOrDefault(device => device.IsSelected)?.DisplayName
            ?? snapshot.SelectedDevice?.Model;
        SetControlText(_adbDetail, snapshot.IsReady && !string.IsNullOrWhiteSpace(phoneName)
            ? $"已连接 {phoneName}" : "未连接手机，请在连接管理里连接手机。");
        _phoneStatus.ForeColor = snapshot.State switch
        {
            AndroidConnectionState.Ready => UiPalette.Success,
            AndroidConnectionState.AuthorizationRequired => UiPalette.Warning,
            AndroidConnectionState.WaitingForDevice or AndroidConnectionState.Discovering or
                AndroidConnectionState.Connecting => UiPalette.Info,
            _ => UiPalette.Danger,
        };

        AndroidDeviceDetails? details = snapshot.SelectedDevice;
        SetControlText(_phoneName, details is null ? "手机" : phoneName ?? details.Model);
        SetControlText(_phoneDetails, details is null ? "未连接"
            : $"ID {details.Model}\nAndroid {details.AndroidVersion} · {FormatDisplaySize(details)}");

        _updatingDeviceList = true;
        try
        {
            ConnectionManagementSnapshot management = _phone is IAndroidConnectionManagementService connectionManagement
                ? connectionManagement.ManagementSnapshot : new(PhoneConnectionMethod.Usb, snapshot.Devices.Select(device => new ManagedAndroidDevice(
                    device.DeviceKey, device.DisplayName, device.Model, device.Transport, true, device.Status, device.IsSelected, false, null)).ToArray());
            if (!_changingConnectionMethod)
            {
                _updatingConnectionMethod = true;
                try { _wirelessView.SelectMethod((WirelessPairingMethod)(int)management.Method); }
                finally { _updatingConnectionMethod = false; }
            }
            _wirelessView.SetDevices(management.Devices);
            if (_wirelessView.DeviceManagerHost.Visible && management.Devices.FirstOrDefault(device => device.DeviceKey == _wirelessView.DeviceManager.DeviceKey) is { } managed)
            { _wirelessView.DeviceManager.SetDevice(managed, management.Method != PhoneConnectionMethod.Usb, !managed.IsAvailable); }
            UpdateVideoProbeButton();
            UpdatePhoneOverlayButton();
            RefreshResolutionSelector();
            UpdateQualityStatus();
        }
        finally
        {
            _updatingDeviceList = false;
        }
    }

    private async void OnRefreshClicked(object? sender, EventArgs eventArgs)
    {
        if (_wirelessOperation is not null && !_qrWaiting) { return; }
        await RunUiOperationAsync(
            "正在重新扫描手机…",
            cancellationToken => _phone.RefreshAsync(cancellationToken).AsTask()).ConfigureAwait(true);
        TryStartAutomaticQr();
    }

    private void OnDeviceListKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.KeyCode != Keys.Enter) { return; }
        args.SuppressKeyPress = true;
        OnDeviceSelected(sender, EventArgs.Empty);
    }

    private async void OnDeviceSelected(object? sender, EventArgs eventArgs)
    {
        if (eventArgs is MouseEventArgs mouse && (mouse.Button != MouseButtons.Left || sender is not ConnectionDeviceList hitList || !Views.WirelessConnectionView.ShouldOpenDevice(hitList, mouse.Location))) { return; }
        if (_updatingDeviceList || _wirelessView.UpdatingDevices || !CanSelectPhoneDevice || _openingDeviceManager ||
            _wirelessView.DeviceManagerHost.Visible || sender is not ListBox list || list.SelectedItem is not ConnectionDeviceListEntry item)
        {
            return;
        }
        _openingDeviceManager = true;
        try
        {
            if (_qrWaiting && _wirelessOperation is not null)
            {
                _qrPausedForDevice = true;
                await _wirelessOperation.CancelAsync();
                if (_wirelessCompletion is { } completion) { await completion.Task; }
            }
            if (IsDisposed || Disposing || !_wirelessView.Visible || !CanSelectPhoneDevice) { return; }
            ManagedAndroidDevice current = (_phone as IAndroidConnectionManagementService)?.ManagementSnapshot.Devices
                .FirstOrDefault(device => device.DeviceKey == item.Device.DeviceKey) ?? item.Device;
            _wirelessView.ShowDeviceManager(current, ReferenceEquals(list, _historyDeviceList));
        }
        catch (Exception) { if (!IsDisposed && !Disposing) { _operationStatus.Text = "设备名称未保存，请重试"; } }
        finally { _openingDeviceManager = false; if (!_wirelessView.DeviceManagerHost.Visible) { _qrPausedForDevice = false; TryStartAutomaticQr(); } }
    }

    private async void OnVideoProbeClicked(object? sender, EventArgs eventArgs)
    {
        await RunUiOperationAsync(
            "正在检查手机视频、解码和显卡纹理…",
            RunVideoProbeAsync).ConfigureAwait(true);
    }

    private async void OnPhoneOverlayClicked(object? sender, EventArgs eventArgs)
    {
        PhoneOverlayState state = _phoneOverlay.Snapshot.State;
        bool closing = state is PhoneOverlayState.Starting or PhoneOverlayState.Running;
        if (!closing && _wirelessOperation is not null && !_qrWaiting) { return; }
        _phoneOverlayButtonProgress = closing
            ? PhoneOverlayButtonProgress.Closing
            : PhoneOverlayButtonProgress.Opening;
        try
        {
            if (closing)
            {
                await RunUiOperationAsync(
                    "正在关闭 SteamVR 手机浮窗…",
                    StopPhoneOverlayAsync)
                    .ConfigureAwait(true);
            }
            else
            {
                await RunUiOperationAsync(
                    "正在打开 SteamVR 手机浮窗…",
                    cancellationToken => StartPhoneOverlayAsync(
                        _phone.Snapshot.SelectedDevice?.DeviceKey,
                        cancellationToken)).ConfigureAwait(true);
            }
        }
        finally
        {
            _phoneOverlayButtonProgress = PhoneOverlayButtonProgress.None;
            if (!IsDisposed && !Disposing)
            {
                UpdatePhoneOverlayButton();
            }
        }
    }

    private async void OnSteamVrBindingsClicked(object? sender, EventArgs eventArgs)
    {
        await RunUiOperationAsync("即将打开桌面的 SteamVR 绑定编辑器；修改完成后返回功能页刷新绑定。", async token =>
        {
            OpenVrBindingResult result = await _phoneOverlay.OpenBindingUiAsync(token).ConfigureAwait(true);
            _operationStatus.Text = result.Succeeded
                ? "已请求打开本软件的 SteamVR 手柄绑定选择页面。"
                : $"{result.Message}　[{result.ReasonCode}]";
            _operationStatus.ForeColor = result.Succeeded ? UiPalette.Success : UiPalette.Danger;
        }, resultTarget: _bindingGuideView.OperationStatus).ConfigureAwait(true);
    }

    private async void OnRestoreDefaultBindingsClicked(object? sender, EventArgs eventArgs)
    {
        if (_operationInProgress) { return; }
        _bindingGuideRevision++; // Ignore any read already in flight.
        _bindingGuideView.BeginRestoreDefaults();
        try
        {
            await RunUiOperationAsync("正在恢复所有手柄的默认绑定并应用…", async token =>
            {
                _bindingGuideView.ControllerSelector.TryGetSelectedValue(out string selectedGroup);
                if (_bindingSynchronization is not null)
                { await _bindingSynchronization.RestoreDefaultsAsync(_detectedController, selectedGroup, token).ConfigureAwait(true); }
                else { await _bindingDefaults.RestoreAsync(_detectedController, selectedGroup, token).ConfigureAwait(true); }
                int revision = ++_bindingGuideRevision;
                if (!_bindingGuideView.ControllerSelector.TryGetSelectedValue(out string selected)) { selected = "pico_controller"; }
                ControllerBindingGuide guide = await ControllerBindingGuideService.ReadAsync(selected, token).ConfigureAwait(true);
                if (!IsDisposed && !Disposing && revision == _bindingGuideRevision) { _bindingGuideView.SetGuide(DescribeSelectedGuide(guide, selected)); }
                _operationStatus.Text = "所有手柄已恢复默认绑定并应用";
                _operationStatus.ForeColor = UiPalette.Success;
            }, resultTarget: _bindingGuideView.OperationStatus).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!IsDisposed && !Disposing)
            {
                _bindingGuideView.OperationStatus.Text = "恢复默认绑定未完成，请重试 [BINDING_DEFAULTS_UI_FAILED]";
                _bindingGuideView.OperationStatus.ForeColor = UiPalette.Danger;
            }
        }
        finally
        {
            if (!IsDisposed && !Disposing) { _bindingGuideView.EndRestoreDefaults(); }
        }
    }

    private async void OnControllerHandChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingSettingsControls || _operationInProgress || _immediateSettingsInProgress ||
            !_controllerHandSelector.TryGetSelectedValue(out ControllerHandPreference hand)) { return; }
        AppSettings videoDraft = _pendingSettings;
        bool applied = false;
        await RunUiOperationAsync("正在切换惯用手…", async cancellationToken =>
        {
            try
            {
                await ApplySettingsAsync(_settings.Snapshot.Value with { ControllerHand = hand }, cancellationToken);
                applied = true;
            }
            finally
            {
                _pendingSettings = SettingsControlPolicy.RestoreVideoDraft(_settings.Snapshot.Value, videoDraft);
                _settingsDirty = HasPendingApplyChanges(_pendingSettings, _settings.Snapshot.Value);
                if (!IsDisposed && !Disposing) { RefreshSettingsControls(); }
            }
        }, resultTarget: _bindingGuideView.OperationStatus).ConfigureAwait(true);
        if (applied && !IsDisposed && !Disposing) { OnBindingGuideRequested(sender, eventArgs); }
    }

    private void OnSettingChoiceChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingSettingsControls ||
            !_controllerHandSelector.TryGetSelectedValue(out ControllerHandPreference hand) ||
            !_resolutionSelector.TryGetSelectedValue(out VideoResolutionProfile resolution) ||
            !_bitrateSelector.TryGetSelectedValue(out int bitrate) ||
            !_frameRateSelector.TryGetSelectedValue(out int frameRate) ||
            !_updateChannelSelector.TryGetSelectedValue(out UpdateChannel updateChannel))
        {
            return;
        }

        _pendingSettings = _pendingSettings with
        {
            ControllerHand = hand,
            VideoResolutionPercent = resolution.Percent,
            VideoBitrateMbps = bitrate,
            VideoMaximumFramesPerSecond = frameRate,
            UpdateChannel = updateChannel,
        };
        _settingsDirty = HasPendingApplyChanges(
            _pendingSettings,
            _settings.Snapshot.Value);
        UpdateQualityStatus();
        UpdateSettingsActionButtons();
    }

    private async void OnImmediateLockScreenSettingChanged(
        object? sender,
        EventArgs eventArgs)
    {
        if (_updatingSettingsControls || _immediateSettingsInProgress)
        {
            return;
        }

        bool keepAwakeWhileGrabbed = _keepAwakeWhileGrabbedToggle.Checked;
        bool vrUnlockKeypadEnabled = _vrUnlockKeypadToggle.Checked;
        await RunUiOperationAsync(
            "正在保存即时设置…",
            async cancellationToken =>
            {
                AppSettings previous = _settings.Snapshot.Value;
                AppSettings candidate = previous with
                {
                    KeepAwakeWhileGrabbed = keepAwakeWhileGrabbed,
                    VrUnlockKeypadEnabled = vrUnlockKeypadEnabled,
                };
                _phoneOverlay.ConfigureLockScreenFeatures(
                    keepAwakeWhileGrabbed,
                    vrUnlockKeypadEnabled);
                try
                {
                    if (candidate != previous)
                    {
                        await _settings.SaveNonMotionAsync(candidate, cancellationToken);
                    }

                    _operationStatus.Text = "即时设置已生效";
                    _operationStatus.ForeColor = UiPalette.Success;
                }
                catch
                {
                    _phoneOverlay.ConfigureLockScreenFeatures(
                        previous.KeepAwakeWhileGrabbed,
                        previous.VrUnlockKeypadEnabled);
                    _pendingSettings = _pendingSettings with
                    {
                        KeepAwakeWhileGrabbed = previous.KeepAwakeWhileGrabbed,
                        VrUnlockKeypadEnabled = previous.VrUnlockKeypadEnabled,
                    };
                    RefreshSettingsControls();
                    throw;
                }
            },
            immediateControlsOnly: true, resultTarget: _settingsView.Status).ConfigureAwait(true);
    }

    private async void OnBindingGuideRequested(object? sender, EventArgs eventArgs)
    {
        if (_bindingGuideView.IsRestoringDefaults) { return; }
        int revision = ++_bindingGuideRevision;
        try
        {
            if (!_bindingGuideView.ControllerSelector.TryGetSelectedValue(out string controller)) { return; }
            _bindingSynchronization?.RequestRetry();
            _bindingGuideView.BeginGuideRead();
            ControllerBindingGuide guide = await ControllerBindingGuideService.ReadAsync(controller, _formLifetime.Token);
            if (!IsDisposed && !Disposing && revision == _bindingGuideRevision)
            {
                _bindingGuideView.SetGuide(DescribeSelectedGuide(guide, controller));
                ShowBindingSynchronizationStatus();
            }
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!IsDisposed && !Disposing && revision == _bindingGuideRevision)
            {
                _bindingGuideView.FailGuideRead();
            }
        }
    }

    private void ConfigurePhoneAutomation()
    {
        AppSettings settings = _settings.Snapshot.Value;
        _phoneControl.ConfigurePhysicalScreen(settings.AutoTurnOffPhoneScreen);
        _mediaSession.ConfigureAutoStart(settings.AutoOpenPhoneOverlay, BuildVideoOptions(settings));
    }

    private async void OnAutomationSettingChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingSettingsControls || _immediateSettingsInProgress || _operationInProgress) { return; }
        AppSettings previous = _settings.Snapshot.Value;
        AppSettings candidate = previous with
        {
            AutoOpenPhoneOverlay = _settingsView.AutoOpenToggle.Checked,
            AutoTurnOffPhoneScreen = _settingsView.ScreenOffToggle.Checked,
            LaunchWithSteamVr = _settingsView.SteamVrToggle.Checked,
            MinimizeAfterOverlayOpened = _settingsView.MinimizeToggle.Checked,
            PicoMicrophoneKeeperEnabled = _settingsView.PicoToggle.Checked,
        };
        await RunUiOperationAsync("正在保存自动化设置…", async cancellationToken =>
        {
            try
            {
                if (candidate.LaunchWithSteamVr != previous.LaunchWithSteamVr)
                {
                    OpenVrBindingResult result = await Task.Run(
                        () => _phoneOverlay.ConfigureSteamVrAutoLaunch(candidate.LaunchWithSteamVr), cancellationToken);
                    if (!result.Succeeded) { throw new SettingsApplyException(result.ReasonCode, result.Message); }
                }
                await _settings.SaveNonMotionAsync(candidate, cancellationToken);
                _settingsView.Status.Text = "自动化设置已保存";
                _settingsView.Status.ForeColor = UiPalette.Success;
            }
            catch
            {
                OpenVrBindingResult? startupRollback = null;
                if (candidate.LaunchWithSteamVr != previous.LaunchWithSteamVr)
                {
                    startupRollback = await Task.Run(
                        () => _phoneOverlay.ConfigureSteamVrAutoLaunch(previous.LaunchWithSteamVr), CancellationToken.None);
                }
                _pendingSettings = _pendingSettings with
                {
                    AutoOpenPhoneOverlay = previous.AutoOpenPhoneOverlay,
                    AutoTurnOffPhoneScreen = previous.AutoTurnOffPhoneScreen,
                    LaunchWithSteamVr = previous.LaunchWithSteamVr,
                    MinimizeAfterOverlayOpened = previous.MinimizeAfterOverlayOpened,
                    PicoMicrophoneKeeperEnabled = previous.PicoMicrophoneKeeperEnabled,
                };
                RefreshSettingsControls();
                _settingsView.Status.Text = "自动化设置未保存，请确认 SteamVR 安装与设置目录可用后重试";
                _settingsView.Status.ForeColor = UiPalette.Danger;
                if (startupRollback is { Succeeded: false })
                {
                    throw new SettingsApplyException(startupRollback.ReasonCode,
                        "设置未保存，SteamVR 自动启动状态也未能恢复；请重新设置此开关。" + startupRollback.Message);
                }
                throw;
            }
        }, immediateControlsOnly: true, resultTarget: _settingsView.Status).ConfigureAwait(true);
    }

    private async void OnApplySettingsClicked(object? sender, EventArgs eventArgs)
    {
        await RunUiOperationAsync(
            "正在统一应用设置…",
            ApplyPendingSettingsAsync, resultTarget: _videoOperationStatus).ConfigureAwait(true);
    }

    private void OnDiscardSettingsClicked(object? sender, EventArgs eventArgs)
    {
        _pendingSettings = _settings.Snapshot.Value;
        _settingsDirty = false;
        RefreshSettingsControls();
        _operationStatus.Text = "已撤销未应用的设置";
        _operationStatus.ForeColor = UiPalette.TextSecondary;
        _videoOperationStatus.Text = _operationStatus.Text;
        _videoOperationStatus.ForeColor = UiPalette.TextSecondary;
    }

    private Task ApplyPendingSettingsAsync(CancellationToken cancellationToken) =>
        ApplySettingsAsync(AppSettingsPolicy.Normalize(_pendingSettings), cancellationToken);

    private async Task ApplySettingsAsync(AppSettings candidate, CancellationToken cancellationToken)
    {
        AppSettings previous = _settings.Snapshot.Value;
        if (candidate == previous)
        {
            _settingsDirty = false;
            UpdateSettingsActionButtons();
            return;
        }

        SettingsApplicationResult result = await _settingsApplication.ApplyAsync(candidate, cancellationToken);
        _operationStatus.Text = result.ScreenRestarted
            ? "设置已统一生效，手机屏幕只重启了一次；电脑音频链路未重启"
            : result.VideoChanged ? "设置已保存，将在下次打开手机浮窗时统一使用"
            : "设置已生效，不需要重启手机屏幕";
        _pendingSettings = _settings.Snapshot.Value;
        _settingsDirty = false;
        _operationStatus.ForeColor = candidate.VideoBitrateMbps == 32 ? UiPalette.Warning : UiPalette.Success;
    }

    private void OnPlayspaceDragChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingSettingsControls)
        {
            return;
        }

        bool enabled = _playspaceDragToggle.Checked;
        try
        {
            _playspaceDrag.SetEnabled(enabled);
            _operationStatus.Text = enabled
                ? "空间拖拽已立即开启（本次运行有效）"
                : "空间拖拽已立即关闭并恢复空间（本次运行有效）";
            _operationStatus.ForeColor = UiPalette.Success;
        }
        catch (InvalidOperationException)
        {
            _operationStatus.Text = "空间拖拽开关切换失败，请重启程序后重试";
            _operationStatus.ForeColor = UiPalette.Danger;
        }
    }

    private async void OnReloadLocalBindingRequested(object? sender, EventArgs eventArgs)
    {
        string? deviceKey = _phone.Snapshot.SelectedDevice?.DeviceKey;
        _bindingRecoveryInProgress = true;
        try
        {
            await RunUiOperationAsync(
                "正在准备本地手柄绑定…",
                async cancellationToken =>
                {
                    await _mediaSession.RestartScreenAsync(
                        deviceKey,
                        BuildVideoOptions(_settings.Snapshot.Value),
                        _ => ReloadLocalBindingAsync(),
                        cancellationToken);
                    _operationStatus.Text = "本地绑定已加载，手机浮窗已重新打开一次";
                    _operationStatus.ForeColor = UiPalette.Success;
                }, resultTarget: _bindingGuideView.OperationStatus).ConfigureAwait(true);
        }
        finally
        {
            _bindingRecoveryInProgress = false;
            if (!IsDisposed && !Disposing)
            {
                UpdateSteamVrBindingNotice(_phoneOverlay.Snapshot);
            }
        }
    }

    private async Task StopPhoneOverlayAsync(CancellationToken cancellationToken)
    {
        await _mediaSession.StopAsync(cancellationToken);
    }

    private async Task StartPhoneOverlayAsync(
        string? deviceKey,
        CancellationToken cancellationToken)
    {
        if (!_mediaSession.CanStart(deviceKey))
        {
            UpdatePhoneOverlayButton();
            throw new PhoneOverlayServiceException(PhoneReasonCodes.MediaStartNotReady,
                "请等待 ADB、已授权手机和 SteamVR 全部就绪后再打开浮窗");
        }
        if (_phoneOverlay.HasSavedBindingChanged() && (_bindingSynchronization is null ||
            !await _bindingSynchronization.SynchronizeNowAsync(cancellationToken).ConfigureAwait(true)))
        {
            await ReloadLocalBindingAsync();
        }

        await _mediaSession.StartAsync(
            deviceKey,
            BuildVideoOptions(_settings.Snapshot.Value),
            cancellationToken);
    }

    private ValueTask ReloadLocalBindingAsync()
        => _bindingSynchronization is null ? ReloadLocalBindingCoreAsync() :
            _bindingSynchronization.PrepareClosedSessionAsync(ReloadLocalBindingCoreAsync, _formLifetime.Token);

    private ValueTask ReloadLocalBindingCoreAsync()
    {
        _playspaceDrag.StopService();
        OpenVrBindingResult result;
        try
        {
            result = _phoneOverlay.ReloadLocalBinding();
        }
        finally
        {
            _playspaceDrag.Start();
        }

        if (!result.Succeeded)
        {
            throw new SettingsApplyException(result.ReasonCode, result.Message);
        }

        return ValueTask.CompletedTask;
    }

    private void OnPhoneAudioStateChanged(object? sender, PhoneAudioChangedEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnPhoneAudioStateChanged(sender, eventArgs));
            return;
        }

        SetControlText(_audioStatus, AudioStateName(eventArgs.Snapshot.State));
        SetControlForeColor(
            _audioStatus,
            eventArgs.Snapshot.State == PhoneAudioState.Playing
                ? UiPalette.Success
                : eventArgs.Snapshot.State == PhoneAudioState.Faulted
                    ? UiPalette.Danger
                : eventArgs.Snapshot.State == PhoneAudioState.Degraded
                    ? UiPalette.Warning
                    : UiPalette.TextSecondary);
    }

    private void OnPhoneControlStateChanged(
        object? sender,
        PhoneControlChangedEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnPhoneControlStateChanged(sender, eventArgs));
            return;
        }

        if (eventArgs.Snapshot.ReasonCode == "SCREEN_GUARD_SAVE_FAILED")
        {
            _operationStatus.Text = eventArgs.Snapshot.Message;
            _operationStatus.ForeColor = UiPalette.Danger;
        }

        SetControlText(_controlStatus, ControlStateName(eventArgs.Snapshot.State));
        SetControlForeColor(
            _controlStatus,
            eventArgs.Snapshot.State == PhoneControlState.Ready
                ? UiPalette.Success
                : eventArgs.Snapshot.State == PhoneControlState.Faulted
                    ? UiPalette.Danger
                    : UiPalette.TextSecondary);
    }

    private void OnPhoneMediaSessionStateChanged(
        object? sender,
        PhoneMediaSessionChangedEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnPhoneMediaSessionStateChanged(sender, eventArgs));
            return;
        }

        bool minimize = _windowPolicy.Observe(
            eventArgs.Snapshot.State == PhoneMediaSessionState.Running,
            eventArgs.Snapshot.State is PhoneMediaSessionState.Stopped or PhoneMediaSessionState.Faulted or PhoneMediaSessionState.WaitingForDevice,
            _settings.Snapshot.Value.MinimizeAfterOverlayOpened);
        if (minimize && _phoneOverlay.Snapshot is { State: PhoneOverlayState.Running, SubmittedFrames: > 0 } &&
            _phoneControl.Snapshot.State == PhoneControlState.Ready && !_closeConfirmation.Visible)
        {
            MinimizeToTray();
        }

        if (eventArgs.Snapshot.State is PhoneMediaSessionState.Starting or PhoneMediaSessionState.WaitingForDevice or
            PhoneMediaSessionState.PausingForScreenRestart or
            PhoneMediaSessionState.ScreenRestartPaused or
            PhoneMediaSessionState.ResumingAfterScreenRestart)
        {
            SetControlText(_operationStatus, eventArgs.Snapshot.Message);
            SetControlForeColor(_operationStatus, UiPalette.Info);
            if (_bindingRecoveryInProgress)
            {
                _steamVrBindingNotice.UpdateNotice(
                    OpenVrBindingHealthState.Loading,
                    true,
                    eventArgs.Snapshot.Message);
            }
        }
    }

    private void OnPlayspaceDragStateChanged(
        object? sender,
        OpenVrPlayspaceDragChangedEventArgs eventArgs)
    {
        try
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(() => OnPlayspaceDragStateChanged(sender, eventArgs));
                return;
            }

            OpenVrPlayspaceDragSnapshot current = _playspaceDrag.Snapshot;
            if (current.State == OpenVrPlayspaceDragState.Ready && _startupSteamVrWarning is not null)
            {
                _startupSteamVrWarning = null;
                UpdateSteamVrBindingNotice(_phoneOverlay.Snapshot);
            }
            bool previousUpdateState = _updatingSettingsControls;
            _updatingSettingsControls = true;
            _playspaceDragToggle.Checked = current.Enabled;

            SetControlText(
                _playspaceDragToggle,
                current.Enabled ? "开启" : "关闭");
            _playspaceDragToggle.BackColor = current.Enabled
                ? UiPalette.ToggleOn
                : UiPalette.ToggleOff;
            SetControlText(
                _playspaceStatus,
                current.Enabled
                    ? current.State == OpenVrPlayspaceDragState.Ready
                        ? current.FlingEnabled ? "弹射模式" : "普通拖拽"
                        : "启用中"
                    : "已关闭");
            SetControlForeColor(
                _playspaceStatus,
                current.Enabled ? UiPalette.Success : UiPalette.TextSecondary);
            _inertiaToggle.Checked = current.FlingEnabled;
            _inertiaToggle.Text = current.FlingEnabled ? "开启" : "关闭";
            UpdatePlayspaceCoordinates(current);
            _updatingSettingsControls = previousUpdateState;
            SyncMotionRuntime(current);
            UpdateOverlayDashboard(_phoneOverlay.Snapshot);
            UpdatePhoneOverlayButton();
            UpdateQualityStatus();
            if (current.Enabled && current.State == OpenVrPlayspaceDragState.Faulted)
            {
                SetControlText(
                    _playspaceStatus,
                    "暂不可用");
                SetControlForeColor(_playspaceStatus, UiPalette.Danger);
            }
        }
        catch (Exception)
        {
            if (!IsDisposed && !Disposing) { _motionView.Status.Text = "移动设置未完成，请重试 [MOTION_UI_FAILED]"; }
        }
    }

    private async void OnPhoneOverlayStateChanged(object? sender, PhoneOverlayChangedEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnPhoneOverlayStateChanged(sender, eventArgs));
            return;
        }

        UpdatePhoneOverlayButton();
        UpdateVideoProbeButton();
        UpdateSteamVrBindingNotice(eventArgs.Snapshot);
        UpdateOverlayDashboard(eventArgs.Snapshot);
        if (eventArgs.Snapshot.Width > 0 && eventArgs.Snapshot.Height > 0 &&
            _resolutionSelector.TryGetSelectedValue(
                out VideoResolutionProfile selectedResolution) &&
            (selectedResolution.ExpectedWidth > selectedResolution.ExpectedHeight) !=
                (eventArgs.Snapshot.Width > eventArgs.Snapshot.Height))
        {
            RefreshResolutionSelector();
        }

        UpdateQualityStatus();
        if (!_bindingGuideView.IsRestoringDefaults && eventArgs.Snapshot.State is PhoneOverlayState.Running or PhoneOverlayState.Faulted)
        {
            SetControlText(_operationStatus, FormatPhoneOverlayStatus(eventArgs.Snapshot));
            SetControlForeColor(
                _operationStatus,
                eventArgs.Snapshot.State == PhoneOverlayState.Running
                    ? UiPalette.Success
                    : UiPalette.Danger);
        }

        if (eventArgs.Snapshot.State == PhoneOverlayState.Running)
        {
            if ((_phoneControl.Snapshot.State is PhoneControlState.Stopped or PhoneControlState.Faulted) &&
                DateTimeOffset.UtcNow >= _nextPhoneControlStartAt)
            {
                _nextPhoneControlStartAt = DateTimeOffset.UtcNow.AddSeconds(3);
                await StartPhoneControlAsync().ConfigureAwait(true);
            }
        }
        else if ((eventArgs.Snapshot.State is PhoneOverlayState.Stopped or PhoneOverlayState.Faulted) &&
            _phoneControl.Snapshot.State is PhoneControlState.Starting or PhoneControlState.Ready)
        {
            try
            {
                await _phoneControl.StopAsync(_formLifetime.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
            {
            }

            _nextPhoneControlStartAt = DateTimeOffset.MinValue;
        }
    }

    private async Task StartPhoneControlAsync()
    {
        try
        {
            await _phoneControl.StartAsync(
                _phone.Snapshot.SelectedDevice?.DeviceKey,
                _formLifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
        }
        catch (PhoneControlServiceException exception)
        {
            _operationStatus.Text = $"{exception.Message}　[{exception.ReasonCode}]";
            _operationStatus.ForeColor = UiPalette.Danger;
        }
    }

    private async Task RunVideoProbeAsync(CancellationToken cancellationToken)
    {
        string? deviceKey = _phone.Snapshot.SelectedDevice?.DeviceKey;
        PhoneVideoDecodeProbeResult result = await _videoProbe.ProbeAsync(
            deviceKey,
            BuildVideoOptions(_settings.Snapshot.Value),
            cancellationToken);
        _operationStatus.Text =
            $"视频与显卡链路正常：{AndroidDisplayNames.Codec(result.Codec)} → {result.PixelFormat} → D3D11 RGBA，" +
            $"{result.Width}×{result.Height}，步幅 {result.Stride}，" +
            $"{result.EncodedPacketCount} 个编码包 / {Math.Max(1, result.EncodedPayloadBytes / 1024)} KiB，" +
            $"解码帧 {Math.Max(1, result.DecodedFrameBytes / 1024)} KiB，纹理槽 {result.TextureSlot}";
        _operationStatus.ForeColor = UiPalette.Success;
    }

    private AndroidVideoOptions BuildVideoOptions(AppSettings settings) =>
        PhoneVideoOptionsFactory.Create(settings, _phone.Snapshot.SelectedDevice, _phoneOverlay.Snapshot);

    private (int Width, int Height) GetNativeDisplayDimensions()
    {
        AndroidDeviceDetails? device = _phone.Snapshot.SelectedDevice;
        int width = device?.NativeDisplayWidth ?? 0;
        int height = device?.NativeDisplayHeight ?? 0;
        PhoneOverlaySnapshot overlay = _phoneOverlay.Snapshot;
        if (width > 0 && height > 0 && overlay.Width > 0 && overlay.Height > 0 &&
            (width > height) != (overlay.Width > overlay.Height))
        {
            (width, height) = (height, width);
        }

        return (width, height);
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnSettingsChanged(sender, eventArgs));
            return;
        }

        AppSettings persisted = eventArgs.Snapshot.Value;
        AppSettings previous = _lastPersistedSettings;
        bool motionOnly = MotionPreferences.From(_lastPersistedSettings).Apply(persisted) == _lastPersistedSettings;
        _lastPersistedSettings = persisted;
        if (motionOnly)
        {
            if (!_motionWriter.HasPending) { ApplyMotionRuntime(MotionPreferences.From(persisted)); }
            _pendingSettings = MotionPreferences.From(persisted).Apply(_pendingSettings);
            RefreshMotionSettings();
            return;
        }
        if (!_motionWriter.HasPending) { ApplyMotionRuntime(MotionPreferences.From(persisted)); }
        _pendingSettings = _settingsDirty ? SettingsDraftMerge.Merge(_pendingSettings, previous, persisted) : persisted;
        _settingsDirty = HasPendingApplyChanges(_pendingSettings, persisted);
        _phoneOverlay.ConfigureLockScreenFeatures(
            persisted.KeepAwakeWhileGrabbed,
            persisted.VrUnlockKeypadEnabled);
        ConfigurePhoneAutomation();
        RefreshSettingsControls();
    }

    private void RefreshSettingsControls()
    {
        bool previousUpdateState = _updatingSettingsControls;
        _updatingSettingsControls = true;
        try
        {
            RefreshMotionSettings();
            AppSettings settings = _pendingSettings;
            _settingsView.AutoOpenToggle.Checked = settings.AutoOpenPhoneOverlay;
            _settingsView.ScreenOffToggle.Checked = settings.AutoTurnOffPhoneScreen;
            _settingsView.SteamVrToggle.Checked = settings.LaunchWithSteamVr;
            _settingsView.MinimizeToggle.Checked = settings.MinimizeAfterOverlayOpened;
            _settingsView.PicoToggle.Checked = settings.PicoMicrophoneKeeperEnabled;
            foreach (ModernToggle toggle in new[] { _settingsView.AutoOpenToggle, _settingsView.ScreenOffToggle, _settingsView.SteamVrToggle, _settingsView.MinimizeToggle, _settingsView.PicoToggle })
            {
                toggle.Text = toggle.Checked ? "开启" : "关闭";
            }
            _ = _controllerHandSelector.SelectValue(settings.ControllerHand);
            _ = _bitrateSelector.SelectValue(settings.VideoBitrateMbps);
            _ = _frameRateSelector.SelectValue(settings.VideoMaximumFramesPerSecond);
            _ = _updateChannelSelector.SelectValue(settings.UpdateChannel);
            _keepAwakeWhileGrabbedToggle.Checked = settings.KeepAwakeWhileGrabbed;
            _keepAwakeWhileGrabbedToggle.Text = settings.KeepAwakeWhileGrabbed
                ? "开启"
                : "关闭";
            _vrUnlockKeypadToggle.Checked = settings.VrUnlockKeypadEnabled;
            _vrUnlockKeypadToggle.Text = settings.VrUnlockKeypadEnabled
                ? "开启"
                : "关闭";
            _playspaceDragToggle.Checked = _playspaceDrag.Snapshot.Enabled;
            _playspaceDragToggle.Text = _playspaceDragToggle.Checked ? "开启" : "关闭";
            _playspaceDragToggle.BackColor = _playspaceDragToggle.Checked
                ? UiPalette.ToggleOn
                : UiPalette.ToggleOff;
            RefreshResolutionSelector();
            UpdateQualityStatus();
            UpdateSettingsActionButtons();
        }
        finally
        {
            _updatingSettingsControls = previousUpdateState;
        }
    }

    private void RefreshResolutionSelector()
    {
        bool previousUpdateState = _updatingSettingsControls;
        _updatingSettingsControls = true;
        try
        {
            int requestedPercent = _pendingSettings.VideoResolutionPercent;
            (int width, int height) = GetNativeDisplayDimensions();
            if (width < 1 || height < 1)
            {
                VideoResolutionProfile unavailable = new(requestedPercent, 1, 1, 0, false);
                _resolutionSelector.SetNodes(
                [
                    new(unavailable, "等待手机"),
                ],
                unavailable);
                _resolutionSelector.Enabled = false;
                return;
            }

            IReadOnlyList<VideoResolutionProfile> profiles =
                VideoResolutionProfiles.Create(width, height);
            VideoResolutionProfile selected = VideoResolutionProfiles.Resolve(
                width,
                height,
                requestedPercent);
            _resolutionSelector.SetNodes(
                profiles
                    .OrderBy(profile => profile.Percent)
                    .Select(profile => new FixedChoiceNode<VideoResolutionProfile>(
                        profile,
                        FormatResolutionChoice(profile))),
                profiles.First(profile => profile.Percent == selected.Percent));
            _resolutionSelector.Enabled = SettingsControlPolicy.CanUseResolutionSelector(
                _operationInProgress,
                width,
                height);
        }
        finally
        {
            _updatingSettingsControls = previousUpdateState;
        }
    }

    private void UpdateQualityStatus()
    {
        SetControlText(_qualityStatus, _settingsDirty ? "待应用 · 将重启浮窗" : "已保存");
    }

    private void UpdateSettingsActionButtons()
    {
        bool enabled = SettingsControlPolicy.CanUsePendingSettingsActions(
            _operationInProgress,
            _immediateSettingsInProgress,
            _settingsDirty);
        _applySettingsButton.Enabled = enabled;
        _discardSettingsButton.Enabled = enabled;
    }

    private static bool HasPendingApplyChanges(AppSettings pending, AppSettings persisted) =>
        pending.ControllerHand != persisted.ControllerHand ||
        pending.VideoResolutionPercent != persisted.VideoResolutionPercent ||
        pending.VideoBitrateMbps != persisted.VideoBitrateMbps ||
        pending.VideoMaximumFramesPerSecond != persisted.VideoMaximumFramesPerSecond ||
        pending.UpdateChannel != persisted.UpdateChannel;

    private static void SetControlText(Control control, string text)
    {
        if (!string.Equals(control.Text, text, StringComparison.Ordinal))
        {
            control.Text = text;
        }
    }

    private static void SetControlForeColor(Control control, Color color)
    {
        if (control.ForeColor != color)
        {
            control.ForeColor = color;
        }
    }

    private async Task RunUiOperationAsync(
        string progressMessage,
        Func<CancellationToken, Task> operation,
        bool immediateControlsOnly = false,
        Label? resultTarget = null)
    {
        if (_uiOperations.IsClosing || IsDisposed || Disposing) { return; }
        using UiOperationLifetime.Lease? lease = _uiOperations.TryEnter(_formLifetime.Token);
        if (lease is null) { return; }
        if (immediateControlsOnly)
        {
            SetImmediateSettingControls(enabled: false);
        }
        else
        {
            SetOperationControls(enabled: false);
        }

        _operationStatus.Text = progressMessage;
        _operationStatus.ForeColor = UiPalette.Info;
        if (resultTarget is not null)
        {
            resultTarget.Text = progressMessage;
            resultTarget.ForeColor = UiPalette.Info;
        }
        try
        {
            await operation(lease.Token);
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (_operationStatus.Text == progressMessage)
            {
                _operationStatus.Text = "操作完成";
                _operationStatus.ForeColor = UiPalette.Success;
            }
        }
        catch (OperationCanceledException) when (_formLifetime.IsCancellationRequested)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            _operationStatus.Text = "操作已取消";
        }
        catch (OperationCanceledException)
        {
            _operationStatus.Text = "操作已取消";
        }
        catch (AndroidConnectionException exception)
        {
            _operationStatus.Text = $"{exception.Message}　[{exception.ReasonCode}]";
            _operationStatus.ForeColor = UiPalette.Danger;
        }
        catch (PhoneVideoProbeException exception)
        {
            _operationStatus.Text = $"{exception.Message}　[{exception.ReasonCode}]";
            _operationStatus.ForeColor = UiPalette.Danger;
        }
        catch (PhoneOverlayServiceException exception)
        {
            _operationStatus.Text = $"{exception.Message}　[{exception.ReasonCode}]";
            _operationStatus.ForeColor = UiPalette.Danger;
        }
        catch (PhoneControlServiceException exception)
        {
            _operationStatus.Text = $"{exception.Message}　[{exception.ReasonCode}]";
            _operationStatus.ForeColor = UiPalette.Danger;
        }
        catch (SettingsApplyException exception)
        {
            _operationStatus.Text = $"{exception.Message}　[{exception.ReasonCode}]";
            _operationStatus.ForeColor = UiPalette.Danger;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _operationStatus.Text = "设置或连接操作失败，请检查本地设置目录权限后重试";
            _operationStatus.ForeColor = UiPalette.Danger;
        }
        catch (Exception)
        {
            _operationStatus.Text = "操作遇到未预期错误　[UI_OPERATION_FAILED]";
            _operationStatus.ForeColor = UiPalette.Danger;
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                if (resultTarget is not null)
                {
                    resultTarget.Text = _operationStatus.Text;
                    resultTarget.ForeColor = _operationStatus.ForeColor;
                }
                if (immediateControlsOnly)
                {
                    SetImmediateSettingControls(enabled: true);
                }
                else
                {
                    SetOperationControls(enabled: true);
                }

                UpdateQualityStatus();
                UpdateSettingsActionButtons();
            }
        }
    }

    private void SetOperationControls(bool enabled)
    {
        _operationInProgress = !enabled;
        _bindingGuideView.RestoreBindingsButton.Enabled = enabled;
        _bindingGuideView.RefreshButton.Enabled = enabled;
        _bindingGuideView.ControllerSelector.Enabled = enabled;
        _wirelessView.SetManagementBusy(!enabled);
        _refreshButton.Enabled = enabled && (_wirelessOperation is null || _qrWaiting);
        UpdateVideoProbeButton();
        UpdatePhoneOverlayButton();
        _steamVrBindingNotice.Enabled = enabled;
        _keepAwakeWhileGrabbedToggle.Enabled = enabled;
        _vrUnlockKeypadToggle.Enabled = enabled;
        _motionView.Strength.Enabled = enabled;
        _motionView.Gravity.Enabled = enabled;
        _motionView.Friction.Enabled = enabled;
        _motionView.ResetMode.Enabled = enabled;
        _settingsView.AutoOpenToggle.Enabled = enabled;
        _settingsView.ScreenOffToggle.Enabled = enabled;
        _settingsView.MinimizeToggle.Enabled = enabled;
        _settingsView.PicoToggle.Enabled = enabled;
        _settingsView.SteamVrToggle.Enabled = enabled;
        _controllerHandSelector.Enabled = enabled;
        _playspaceDragToggle.Enabled = enabled;
        (int width, int height) = GetNativeDisplayDimensions();
        _resolutionSelector.Enabled = SettingsControlPolicy.CanUseResolutionSelector(
            _operationInProgress,
            width,
            height);
        _bitrateSelector.Enabled = enabled;
        _frameRateSelector.Enabled = enabled;
        _updateChannelSelector.Enabled = false;
        UpdateSettingsActionButtons();
    }

    private void SetImmediateSettingControls(bool enabled)
    {
        _immediateSettingsInProgress = !enabled;
        _motionView.Strength.Enabled = enabled;
        _motionView.Gravity.Enabled = enabled;
        _motionView.Friction.Enabled = enabled;
        _motionView.ResetMode.Enabled = enabled;
        _settingsView.AutoOpenToggle.Enabled = enabled;
        _settingsView.ScreenOffToggle.Enabled = enabled;
        _settingsView.MinimizeToggle.Enabled = enabled;
        _settingsView.PicoToggle.Enabled = enabled;
        _settingsView.SteamVrToggle.Enabled = enabled;
        _keepAwakeWhileGrabbedToggle.Enabled = enabled;
        _vrUnlockKeypadToggle.Enabled = enabled;
        UpdateSettingsActionButtons();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (WindowsWindowChrome.TryApply(Handle))
        {
            Region? previousDwmRegion = Region;
            Region = null;
            previousDwmRegion?.Dispose();
            return;
        }

        Rectangle bounds = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using GraphicsPath path = SurfacePanel.CreateRoundedRectangle(bounds, 14);
        Region? previousRegion = Region;
        Region = new Region(path);
        previousRegion?.Dispose();
    }

    private void OnFormClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        _formLifetime.Cancel();
        if (_bindingSynchronization is not null) { _bindingSynchronization.Changed -= OnBindingSynchronizationChanged; }
        _runtime.StateChanged -= OnRuntimeStateChanged;
        _phone.StateChanged -= OnPhoneStateChanged;
        _phoneOverlay.StateChanged -= OnPhoneOverlayStateChanged;
        _phoneAudio.StateChanged -= OnPhoneAudioStateChanged;
        _phoneControl.StateChanged -= OnPhoneControlStateChanged;
        _mediaSession.StateChanged -= OnPhoneMediaSessionStateChanged;
        _inertiaToggle.CheckedChanged -= OnMotionParametersChanged;
        _motionView.ParametersChanged -= OnMotionParametersChanged;
        _motionWriter.Saved -= OnMotionSaved;
        _playspaceDrag.StateChanged -= OnPlayspaceDragStateChanged;
        _settings.Changed -= OnSettingsChanged;
        _deviceList.MouseClick -= OnDeviceSelected;
        _deviceList.KeyDown -= OnDeviceListKeyDown;
        _historyDeviceList.MouseClick -= OnDeviceSelected;
        _historyDeviceList.KeyDown -= OnDeviceListKeyDown;
        _refreshButton.Click -= OnRefreshClicked;
        _videoProbeButton.Click -= OnVideoProbeClicked;
        _phoneOverlayButton.Click -= OnPhoneOverlayClicked;
        _steamVrBindingsButton.Click -= OnSteamVrBindingsClicked;
        _checkUpdateButton.Click -= OnCheckUpdateClicked;
        _prepareDiagnosticsButton.Click -= OnPrepareDiagnosticsClicked;
        _uploadDiagnosticsButton.Click -= OnUploadDiagnosticsClicked;
        _cancelDiagnosticReportButton.Click -= OnCancelDiagnosticReportClicked;
        _diagnosticIssueTypeComboBox.SelectedValueChanged -= OnDiagnosticIssueTypeChanged;
        _keepAwakeWhileGrabbedToggle.CheckedChanged -= OnImmediateLockScreenSettingChanged;
        _vrUnlockKeypadToggle.CheckedChanged -= OnImmediateLockScreenSettingChanged;
        _steamVrBindingNotice.ReloadRequested -= OnReloadLocalBindingRequested;
        _controllerHandSelector.SelectedValueChanged -= OnControllerHandChanged;
        _playspaceDragToggle.CheckedChanged -= OnPlayspaceDragChanged;
        _resolutionSelector.SelectedValueChanged -= OnSettingChoiceChanged;
        _bitrateSelector.SelectedValueChanged -= OnSettingChoiceChanged;
        _frameRateSelector.SelectedValueChanged -= OnSettingChoiceChanged;
        _updateChannelSelector.SelectedValueChanged -= OnSettingChoiceChanged;
        _applySettingsButton.Click -= OnApplySettingsClicked;
        _discardSettingsButton.Click -= OnDiscardSettingsClicked;
        foreach (Control dragSurface in _windowDragSurfaces)
        {
            dragSurface.MouseDown -= OnTitleBarMouseDown;
            dragSurface.MouseMove -= OnTitleBarMouseMove;
            dragSurface.MouseUp -= OnTitleBarMouseUp;
        }

        if (_activeDragSurface is not null)
        {
            _activeDragSurface.Capture = false;
            _activeDragSurface = null;
        }

        _windowDragSurfaces.Clear();
        Icon? windowIcon = _windowIcon;
        _windowIcon = null;
        Icon = null;
        windowIcon?.Dispose();
        FormClosed -= OnFormClosed;
        Shown -= OnControllerDiscoveryShown;
        Shown -= OnSteamVrStartupWarningShown;
        _formLifetime.Dispose();
    }

    private void UpdatePhoneOverlayButton()
    {
        _deviceList.Enabled = CanSelectPhoneDevice && _deviceList.Items.Count > 0;
        _historyDeviceList.Enabled = CanSelectPhoneDevice && _historyDeviceList.Items.Count > 0;
        PhoneOverlayState state = _phoneOverlay.Snapshot.State;
        PhoneOverlayButtonProgress progress = _phoneOverlayButtonProgress !=
            PhoneOverlayButtonProgress.None
                ? _phoneOverlayButtonProgress
                : state switch
                {
                    PhoneOverlayState.Starting => PhoneOverlayButtonProgress.Opening,
                    PhoneOverlayState.Stopping => PhoneOverlayButtonProgress.Closing,
                    _ => PhoneOverlayButtonProgress.None,
                };
        bool canStop = state is PhoneOverlayState.Starting or PhoneOverlayState.Running;
        _steamVrBindingsButton.Enabled = !_operationInProgress;
        _playspaceDragToggle.Enabled = !_operationInProgress;
        if (progress != PhoneOverlayButtonProgress.None)
        {
            SetControlText(
                _phoneOverlayButton,
                progress == PhoneOverlayButtonProgress.Opening
                    ? "正在打开"
                    : "正在关闭");
            _phoneOverlayButton.Icon = UiIcons.LoadingDots;
            _phoneOverlayButton.Tone = UiButtonTone.Neutral;
            _phoneOverlayButton.Enabled = false;
            return;
        }

        SetControlText(
            _phoneOverlayButton,
            canStop ? "关闭手机浮窗" : "开启手机浮窗");
        _phoneOverlayButton.Icon = canStop ? UiIcons.Pause : UiIcons.Play;
        bool ready = _mediaSession.CanStart(_phone.Snapshot.SelectedDevice?.DeviceKey);
        PhoneOverlayButtonPolicy.Apply(_phoneOverlayButton, canStop, ready, _operationInProgress,
            wirelessBusy: _wirelessOperation is not null && !_qrWaiting);
    }

    private void UpdateVideoProbeButton()
    {
        PhoneOverlayState overlayState = _phoneOverlay.Snapshot.State;
        bool overlayInactive = overlayState is PhoneOverlayState.Stopped or PhoneOverlayState.Faulted;
        _videoProbeButton.Enabled = !_operationInProgress &&
            overlayInactive &&
            _phone.Snapshot.State == AndroidConnectionState.Ready;
    }

    private void UpdateDashboardSnapshots()
    {
        PhoneAudioSnapshot audio = _phoneAudio.Snapshot;
        SetControlText(_audioStatus, AudioStateName(audio.State));
        SetControlForeColor(
            _audioStatus,
            audio.State == PhoneAudioState.Playing
                ? UiPalette.Success
                : audio.State == PhoneAudioState.Faulted
                    ? UiPalette.Danger
                : audio.State == PhoneAudioState.Degraded
                    ? UiPalette.Warning
                    : UiPalette.TextSecondary);

        PhoneControlSnapshot control = _phoneControl.Snapshot;
        SetControlText(_controlStatus, ControlStateName(control.State));
        SetControlForeColor(
            _controlStatus,
            control.State == PhoneControlState.Ready
                ? UiPalette.Success
                : control.State == PhoneControlState.Faulted
                    ? UiPalette.Danger
                    : UiPalette.TextSecondary);

        OpenVrPlayspaceDragSnapshot playspace = _playspaceDrag.Snapshot;
        SetControlText(_playspaceStatus, playspace.Enabled ? "已启用" : "已关闭");
        SetControlForeColor(
            _playspaceStatus,
            playspace.Enabled ? UiPalette.Success : UiPalette.TextSecondary);
        UpdatePlayspaceCoordinates(playspace);
        UpdateOverlayDashboard(_phoneOverlay.Snapshot);
    }

    private void UpdatePlayspaceCoordinates(OpenVrPlayspaceDragSnapshot snapshot)
    {
        if (snapshot.ReasonCode == OpenVrReasonCodes.PlayspaceHeightResetUnavailable)
        {
            SetControlText(_playspaceStatus, "复位失败");
            SetControlForeColor(_playspaceStatus, UiPalette.Warning);
            SetControlText(_playspaceCoordinates, "地面坐标不可用，请重试");
            return;
        }
        if (snapshot.State is OpenVrPlayspaceDragState.Stopped or
            OpenVrPlayspaceDragState.Faulted)
        {
            SetControlText(_playspaceCoordinates, "X --   Y --   Z --");
            return;
        }

        SetControlText(
            _playspaceCoordinates,
            $"X {snapshot.OffsetX:0.00}  Y {snapshot.OffsetY:0.00}  " +
            $"Z {snapshot.OffsetZ:0.00} m");
    }

    private void UpdateOverlayDashboard(PhoneOverlaySnapshot snapshot)
    {
        string videoState = snapshot.State switch
        {
            PhoneOverlayState.Running => "已就绪",
            PhoneOverlayState.Starting => "连接中",
            PhoneOverlayState.Stopping => "关闭中",
            PhoneOverlayState.Faulted => "异常",
            _ => "未运行",
        };
        SetControlText(_videoStatus, videoState);
        SetControlForeColor(
            _videoStatus,
            snapshot.State == PhoneOverlayState.Running
                ? UiPalette.Success
                : snapshot.State == PhoneOverlayState.Faulted
                    ? UiPalette.Danger
                    : UiPalette.TextSecondary);

        string steamVrState = SteamVrStatusPresentation.Text(snapshot, _playspaceDrag.Snapshot.State, _startupSteamVrWarning);
        SetControlText(_steamVrStatus, steamVrState);
        SetControlForeColor(_steamVrStatus, steamVrState is "已就绪" or "已连接"
            ? UiPalette.Success : steamVrState is "连接异常" or "连接超时" ? UiPalette.Danger : UiPalette.TextSecondary);

        SetControlText(
            _metricResolution,
            snapshot.Width > 0 && snapshot.Height > 0
                ? $"{snapshot.Width}×{snapshot.Height}"
                : "--");
        SetControlText(
            _metricBitrate,
            snapshot.AverageBitrateMbps > 0
                ? $"{snapshot.AverageBitrateMbps:F2} Mbps"
                : "--");
        SetControlText(
            _metricFrameRate,
            snapshot.FramesPerSecond > 0
                ? $"{snapshot.FramesPerSecond:F1} FPS"
                : "--");
        SetControlText(
            _metricLatency,
            snapshot.AverageLatencyMilliseconds > 0
                ? $"{snapshot.AverageLatencyMilliseconds:F0} ms"
                : "--");
    }

    private static string ConnectionStateName(AndroidConnectionState state) => state switch
    {
        AndroidConnectionState.Ready => "已连接",
        AndroidConnectionState.Discovering => "扫描中",
        AndroidConnectionState.Connecting or AndroidConnectionState.Reconnecting => "连接中",
        AndroidConnectionState.AuthorizationRequired => "等待授权",
        AndroidConnectionState.WaitingForDevice => "等待设备",
        AndroidConnectionState.Offline => "设备离线",
        AndroidConnectionState.Faulted => "连接异常",
        _ => "未连接",
    };

    private static string AudioStateName(PhoneAudioState state) => state switch
    {
        PhoneAudioState.Playing => "已就绪",
        PhoneAudioState.Starting => "连接中",
        PhoneAudioState.Degraded => "已降级",
        PhoneAudioState.Faulted => "异常",
        PhoneAudioState.Stopping => "关闭中",
        _ => "未运行",
    };

    private static string ControlStateName(PhoneControlState state) => state switch
    {
        PhoneControlState.Ready => "已就绪",
        PhoneControlState.Starting => "连接中",
        PhoneControlState.Stopping => "关闭中",
        PhoneControlState.Faulted => "异常",
        _ => "未运行",
    };

    private static string FormatDisplaySize(AndroidDeviceDetails details) =>
        details.NativeDisplayWidth > 0 && details.NativeDisplayHeight > 0
            ? $"{details.NativeDisplayWidth} × {details.NativeDisplayHeight}"
            : "分辨率未知";

    private static string FormatPhoneOverlayStatus(PhoneOverlaySnapshot snapshot)
    {
        if (snapshot.State != PhoneOverlayState.Running)
        {
            return $"{snapshot.Message}　[{snapshot.ReasonCode}]";
        }

        string frameRate = snapshot.FramesPerSecond > 0
            ? $"，{snapshot.FramesPerSecond:F1} FPS"
            : string.Empty;
        string pose = snapshot.WorldAnchored ? "固定在 VR 世界" : "等待头显定位";
        string input = snapshot.SteamVrInputReady
            ? snapshot.OverlayGrabbed
                ? "正在抓取"
                : snapshot.ControllerHovered
                    ? "手柄已指向"
                    : "手柄已就绪"
            : $"手柄未就绪 {snapshot.InputReasonCode}";
        return $"{snapshot.Message}：{snapshot.Width}×{snapshot.Height}，已提交 {snapshot.SubmittedFrames} 帧{frameRate}；{pose}；{input}";
    }

    private static string FormatAudioStatus(PhoneAudioSnapshot snapshot)
    {
        string buffer = snapshot.State == PhoneAudioState.Playing
            ? $"，缓冲 {snapshot.BufferedDuration.TotalMilliseconds:F0} ms"
            : string.Empty;
        return $"手机音频：{snapshot.Message}{buffer}　[{snapshot.ReasonCode}]";
    }

    private static string FormatResolutionChoice(VideoResolutionProfile profile) =>
        profile.Percent == 100
            ? $"原生 {profile.ExpectedWidth}×{profile.ExpectedHeight}"
            : $"{(profile.IsApproximate ? "约" : string.Empty)}{profile.Percent}% " +
              $"{profile.ExpectedWidth}×{profile.ExpectedHeight}";

    private void UpdateSteamVrBindingNotice(PhoneOverlaySnapshot snapshot)
    {
        if (_bindingRecoveryInProgress)
        {
            return;
        }

        if (snapshot.BindingState == OpenVrBindingHealthState.Ready) { _startupSteamVrWarning = null; }
        if (_startupSteamVrWarning is { } warning)
        {
            _bindingGuideView.BindingNotice.UpdateConnectionNotice(warning.Message);
            return;
        }
        _bindingGuideView.UpdateBindingNotice(
            snapshot.BindingState,
            snapshot.ShowBindingNotice,
            snapshot.BindingMessage);
    }

    private static string CapabilityName(AndroidCapability capability) => capability.State switch
    {
        AndroidCapabilityState.Available => "可用",
        AndroidCapabilityState.Unavailable => "不可用",
        _ => "待探测",
    };

    private static OpenVrControllerHand MapControllerHand(ControllerHandPreference hand) =>
        hand == ControllerHandPreference.Left
            ? OpenVrControllerHand.Left
            : OpenVrControllerHand.Right;

}
