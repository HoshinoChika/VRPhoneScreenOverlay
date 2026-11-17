using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.App.Views;

public partial class TranslationSettingsView : UserControl
{
    private void PaintServerLamp(object? sender, PaintEventArgs e)
    {
        if (sender is not Label lamp) { return; }
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float diameter = Math.Min(lamp.Width, lamp.Height) / 3f;
        using var brush = new SolidBrush(lamp.ForeColor);
        e.Graphics.FillEllipse(brush, (lamp.Width - diameter) / 2f, (lamp.Height - diameter) / 2f, diameter, diameter);
    }
    internal event EventHandler? BackRequested;
    private TranslationConfigurationStore? _configuration;
    private Func<string, CancellationToken, Task>? _validate;
    private CancellationTokenSource? _validation;
    private bool _syncingSource;
    private bool _savingSource;

    private async void OnSourceChanged(object? sender, EventArgs e)
    {
        if (_syncingSource || _savingSource || sender is not RadioButton { Checked: true } selected) { return; }
        _syncingSource = true;
        if (ReferenceEquals(selected, builtIn)) { personal.Checked = false; } else { builtIn.Checked = false; }
        _syncingSource = false;
        if (_configuration is null) { RefreshApiState(); SetBusy(false); return; }
        _savingSource = true;
        SetBusy(true);
        try
        {
            await _configuration.SavePreferencesAsync(_configuration.Preferences with
            { ApiSource = builtIn.Checked ? TranslationApiSource.BuiltIn : TranslationApiSource.Personal }, CancellationToken.None);
            status.Text = string.Empty;
        }
        catch (Exception) { status.Text = "来源保存失败，请重试"; }
        finally { _savingSource = false; if (!IsDisposed) { RefreshApiState(); SetBusy(false); } }
    }
    private CancellationTokenSource? _serverCheck;
    private Func<CancellationToken, Task<bool>>? _checkServer;
    internal void ConfigureServerCheck(Func<CancellationToken, Task<bool>> check) => _checkServer = check;

    private async void OnPageVisibilityChanged(object? sender, EventArgs e)
    {
        _serverCheck?.Cancel();
        if (!Visible || _checkServer is null) { return; }
        using var cancellation = new CancellationTokenSource();
        _serverCheck = cancellation;
        serverLamp.ForeColor = UiPalette.TextSecondary;
        builtInState.Text = "正在连接服务器";
        try
        {
            bool connected = await _checkServer(cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) { return; }
            serverLamp.ForeColor = connected ? UiPalette.Success : UiPalette.Danger;
            builtInState.Text = connected ? "已连接服务器" : "无法连接服务器";
        }
        catch (Exception)
        {
            if (!IsDisposed && !cancellation.IsCancellationRequested)
            { serverLamp.ForeColor = UiPalette.Danger; builtInState.Text = "无法连接服务器"; }
        }
        finally { if (ReferenceEquals(_serverCheck, cancellation)) { _serverCheck = null; } }
    }

    public TranslationSettingsView()
    {
        InitializeComponent();
        VisibleChanged += OnPageVisibilityChanged;
        builtIn.CheckedChanged += OnSourceChanged;
        personal.CheckedChanged += OnSourceChanged;
        RefreshApiState();
        SetBusy(false);
        paste.Click += (_, _) =>
        {
            try { if (Clipboard.ContainsText()) { key.Text = Clipboard.GetText(); } }
            catch (System.Runtime.InteropServices.ExternalException) { status.Text = "无法读取剪贴板，请使用 Ctrl+V 重试。"; }
        };
        cancel.Click += (_, _) => { _validation?.Cancel(); BackRequested?.Invoke(this, EventArgs.Empty); };
        save.Click += SaveClicked;
        clear.Click += ClearClicked;
    }

    internal void Configure(TranslationConfigurationStore configuration, Func<string, CancellationToken, Task> validate)
    {
        _configuration = configuration;
        _validate = validate;
        RefreshApiState();
        status.Text = string.Empty;
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (_configuration is null || _validate is null || _validation is not null) { return; }
        string candidate = key.Text.Trim();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _validation = cancellation;
        SetBusy(true);
        status.Text = "正在验证图片识别能力…";
        try
        {
            await _validate(candidate, cancellation.Token);
            await _configuration.SaveKeyAsync(candidate, cancellation.Token);
            key.Clear(); status.Text = "验证通过，API Key 已加密保存。";
        }
        catch (OperationCanceledException) { status.Text = "验证已取消或超时，原设置未替换。"; }
        catch (Exception) { status.Text = "验证或保存失败。请检查 Key、余额及网络后重试，原有效 Key 保留。"; }
        finally { _validation = null; if (!IsDisposed) { SetBusy(false); RefreshApiState(); } }
    }

    private async void ClearClicked(object? sender, EventArgs e)
    {
        if (_configuration is null) { return; }
        SetBusy(true);
        try { await Task.Run(_configuration.ClearKey); key.Clear(); status.Text = "API Key 已清除。"; }
        catch (Exception) { status.Text = "清除失败，请检查目录写入权限。"; }
        finally { if (!IsDisposed) { SetBusy(false); RefreshApiState(); } }
    }

    internal void RefreshApiState()
    {
        if (_configuration is not null && !_savingSource)
        {
            _syncingSource = true;
            builtIn.Checked = _configuration.Preferences.ApiSource == TranslationApiSource.BuiltIn;
            personal.Checked = !builtIn.Checked;
            _syncingSource = false;
            SetBusy(_validation is not null);
        }
        bool available = _configuration?.KeyAvailable == true;
        apiLamp.ForeColor = !personal.Checked ? UiPalette.TextSecondary : available ? UiPalette.Success : UiPalette.Danger;
        apiLamp.AccessibleName = available ? "API已配置并通过验证" : "API不可用";
        apiState.Text = available ? "验证通过" :
            _configuration?.KeyFailure is not null ? "验证失效，请重新验证" : "未填写 Key";
    }

    private void SetBusy(bool busy)
    {
        builtIn.Enabled = personal.Enabled = !busy;
        save.Enabled = clear.Enabled = paste.Enabled = key.Enabled = !busy && personal.Checked;
    }
}
