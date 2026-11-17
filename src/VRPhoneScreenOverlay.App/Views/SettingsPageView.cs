using System.ComponentModel;
using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.App.Views;

public partial class SettingsPageView : UserControl
{
    private int _savedSettingsScroll;
    private bool _translationPageOpen;
    internal void ShowTranslationSettings()
    {
        _savedSettingsScroll = settingsScrollBar.Position;
        _translationPageOpen = true;
        settingsViewport.Visible = false;
        settingsScrollBar.Visible = false;
        translationSettings.RefreshApiState();
        translationSettings.Visible = true;
        translationSettings.BringToFront();
    }

    internal void ReturnFromTranslationSettings()
    {
        if (!_translationPageOpen) { return; }
        _translationPageOpen = false;
        translationSettings.Visible = false;
        settingsViewport.Visible = true;
        settingsScrollBar.Visible = true;
        deepSeekButton.Focus();
        settingsScrollBar.ScrollTo(_savedSettingsScroll);
    }
    private PhotoSyncPreferences? _photoSync;
    private bool _syncingPhoto;
    private string? _photoSyncError;
    internal void ConfigurePhotoSync(PhotoSyncPreferences service)
    {
        _photoSync = service;
        _syncingPhoto = true;
        photoSyncToggle.Checked = service.Enabled;
        photoSyncToggle.Text = service.Enabled ? "开启" : "关闭";
        _syncingPhoto = false;
    }
    private async void OnPhotoSyncChanged(object? sender, EventArgs e)
    {
        if (_photoSync is null || _syncingPhoto) { return; }
        _photoSyncError = null;
        photoSyncToggle.Enabled = false;
        try { await _photoSync.SetEnabledAsync(photoSyncToggle.Checked, CancellationToken.None); }
        catch (Exception) { _photoSyncError = "照片同步设置未完成，请重试"; photoSyncStatus.Text = _photoSyncError; }
        finally
        {
            if (!IsDisposed)
            {
                _syncingPhoto = true;
                photoSyncToggle.Checked = _photoSync.Enabled;
                _syncingPhoto = false;
                photoSyncToggle.Enabled = true;
                photoSyncToggle.Text = photoSyncToggle.Checked ? "开启" : "关闭";
            }
        }
    }

    private OutsideInputCommit? _captureCommit;
    private ClipboardConfigurationStore? _clipboardConfiguration;
    private bool _syncingClipboard;
    internal void ConfigureClipboard(ClipboardConfigurationStore configuration)
    {
        _clipboardConfiguration = configuration;
        RefreshClipboard();
    }
    private void RefreshClipboard()
    {
        _syncingClipboard = true;
        autoCopyToggle.Checked = _clipboardConfiguration?.Preferences.AutoCopy == true;
        autoParseToggle.Checked = _clipboardConfiguration?.Preferences.AutoParse == true;
        autoCopyToggle.Text = autoCopyToggle.Checked ? "开启" : "关闭";
        autoParseToggle.Text = autoParseToggle.Checked ? "开启" : "关闭";
        _syncingClipboard = false;
    }
    private async void OnClipboardPreferenceChanged(object? sender, EventArgs e)
    {
        if (_syncingClipboard || _clipboardConfiguration is null) { return; }
        autoCopyToggle.Enabled = autoParseToggle.Enabled = false;
        try
        {
            await _clipboardConfiguration.SaveAsync(new(autoCopyToggle.Checked, autoParseToggle.Checked), CancellationToken.None);
            clipboardStatus.Text = "已生效，仅处理手机新复制的内容";
        }
        catch (Exception) { clipboardStatus.Text = "保存失败，请重试"; }
        finally
        {
            if (!IsDisposed) { RefreshClipboard(); autoCopyToggle.Enabled = autoParseToggle.Enabled = true; }
        }
    }
    private TranslationConfigurationStore? _translationConfiguration;
    private bool _syncingTranslation;
    private bool _savingTranslation;
    internal void ConfigureTranslation(TranslationConfigurationStore configuration, Func<string, CancellationToken, Task> validate)
    {
        _translationConfiguration = configuration;
        translationSettings.Configure(configuration, validate);
        RefreshTranslation();
    }
    private void OnViewportSizeChanged(object? sender, EventArgs e)
    {
        // Clip only the native scrollbar; retain WinForms scrolling and focus navigation.
        settingsModules.SetBounds(0, 0, settingsViewport.ClientSize.Width + SystemInformation.VerticalScrollBarWidth,
            settingsViewport.ClientSize.Height);
    }
    private void OnModulesSizeChanged(object? sender, EventArgs e)
    {
        int width = Math.Max(320, settingsModules.ClientSize.Width - settingsModules.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
        foreach (Control module in settingsModules.Controls) { module.Width = width; }
        ApplyStatusLayout();
    }
    private void RefreshTranslation()
    {
        if (_translationConfiguration is null || _savingTranslation) { return; }
        _syncingTranslation = true;
        try
        {
            translationSoundToggle.Checked = _translationConfiguration.Preferences.ShutterSound;
            translationSoundToggle.Text = translationSoundToggle.Checked ? "开启" : "关闭";
            captureRangeInput.SelectValue(_translationConfiguration.Preferences.CaptureRangePercent);
            translationSettings.RefreshApiState();
        }
        finally { _syncingTranslation = false; }
    }
    private async void OnTranslationSoundChanged(object? sender, EventArgs e)
    {
        translationSoundToggle.Text = translationSoundToggle.Checked ? "开启" : "关闭";
        if (_syncingTranslation || _savingTranslation || _translationConfiguration is null) { return; }
        _savingTranslation = true;
        try
        {
            // Validated can fire while WinForms is transferring focus to a child button.
            // Finish that transition before disabling the container for asynchronous saving.
            await Task.Yield();
            if (IsDisposed || Disposing) { return; }
            translationSoundToggle.Enabled = captureRangeInput.Enabled = false;
            await _translationConfiguration.SavePreferencesAsync(_translationConfiguration.Preferences with { ShutterSound = translationSoundToggle.Checked, CaptureRangePercent = (int)captureRangeInput.Value }, CancellationToken.None);
            translationStatus.Text = string.Empty;
        }
        catch (Exception) { translationStatus.Text = "设置保存失败，请重试"; }
        finally
        {
            _savingTranslation = false;
            if (!IsDisposed) { translationSoundToggle.Enabled = captureRangeInput.Enabled = true; RefreshTranslation(); }
        }
    }
    public SettingsPageView()
    {
        InitializeComponent();
        translationSettings.BackRequested += (_, _) => ReturnFromTranslationSettings();
        Load += (_, _) =>
        {
            if (_captureCommit is null && FindForm() is { } form)
            { _captureCommit = new OutsideInputCommit(form, captureRangeInput); }
        };
        Disposed += (_, _) => { _captureCommit?.Dispose(); _captureCommit = null; };
        foreach (Label label in new[] { status, picoStatus, translationStatus, clipboardStatus, photoSyncStatus })
        { label.TextChanged += (_, _) => ApplyStatusLayout(); }
        settingsScrollBar.Bind(settingsModules);
        OnViewportSizeChanged(this, EventArgs.Empty);
        buttonResetDisabledToggle.CheckedChanged += (_, _) =>
            buttonResetDisabledToggle.Text = buttonResetDisabledToggle.Checked ? "开启" : "关闭";
    }
    private bool _arrangingStatus;
    internal void ApplyStatusLayout()
    {
        if (_arrangingStatus || status is null || clipboardStatus is null) { return; }
        _arrangingStatus = true;
        settingsModules.SuspendLayout();
        try
        {
            Arrange(card, status, minimizeToggle.Bottom);
            Arrange(audioModule, picoStatus, picoToggle.Bottom);
            Arrange(translationModule, translationStatus, deepSeekButton.Bottom);
            Arrange(clipboardModule, clipboardStatus, autoParseToggle.Bottom);
            Arrange(photoSyncModule, photoSyncStatus, photoSyncToggle.Bottom);
        }
        finally { settingsModules.ResumeLayout(true); _arrangingStatus = false; }

        void Arrange(Control module, Label label, int contentBottom)
        {
            int padding = UiLayoutMetrics.Pixels(this, 12);
            bool hasText = !string.IsNullOrWhiteSpace(label.Text);
            label.Visible = hasText;
            label.Top = contentBottom + UiLayoutMetrics.Pixels(this, 8);
            if (hasText)
            {
                label.Height = TextRenderer.MeasureText(label.Text, label.Font,
                    new Size(Math.Max(1, label.Width), int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            }
            module.Height = (hasText ? label.Bottom : contentBottom) + padding;
        }
    }

    internal ModernToggle KeepAwakeWhileGrabbedToggle => keepAwakeToggle;
    internal ModernToggle VrUnlockKeypadToggle => unlockKeypadToggle;
    internal ModernToggle AutoOpenToggle => autoOpenToggle;
    internal ModernToggle ScreenOffToggle => screenOffToggle;
    internal ModernToggle SteamVrToggle => steamVrToggle;
    internal ModernToggle MinimizeToggle => minimizeToggle;
    internal Label Status => status;
    internal TranslationSettingsView TranslationSettings => translationSettings;
    internal ModernToggle PicoToggle => picoToggle;
    internal ModernToggle ButtonResetDisabledToggle => buttonResetDisabledToggle;
    internal Label PicoStatus => picoStatus;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string>? PicoStatusProvider { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<(string Text, bool Warning)>? StartupStatusProvider { get; set; }
    private (string Text, bool Warning)? _lastStartupStatus;
    private void OnPicoStatusTick(object? sender, EventArgs e)
    {
        if (_photoSync is not null) { photoSyncStatus.Text = _photoSyncError ?? (_photoSync.Status is "关闭" or "等待新照片" or "已同步，等待新照片" ? string.Empty : _photoSync.Status); }
        RefreshTranslation();
        if (PicoStatusProvider is not null) { picoStatus.Text = PicoStatusProvider(); }
        if (StartupStatusProvider is not null) { ApplyStartupStatus(StartupStatusProvider()); }
    }

    internal void ApplyStartupStatus((string Text, bool Warning) next)
    {
        if (next == _lastStartupStatus && (!next.Warning || status.Text == next.Text)) { return; }
        bool ownsCurrentMessage = _lastStartupStatus is { } previous && previous.Text.Length > 0 && status.Text == previous.Text;
        if (!ownsCurrentMessage && status.ForeColor == UiPalette.Danger && status.Text.Length > 0) { return; }
        _lastStartupStatus = next;
        // A successful background check clears only its own previous notice.
        // Keep results and failures from other settings operations visible.
        if (!next.Warning && !ownsCurrentMessage) { return; }
        status.Text = next.Text;
        status.ForeColor = next.Warning ? UiPalette.Warning : UiPalette.TextSecondary;
    }
}
