using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.App.Views;

internal sealed record DevicePreferenceDraft(string DeviceKey, string? CustomName, bool AutoConnect);

public partial class ConnectionDeviceView : UserControl
{
    private bool _canConnect;
    private bool _busy;
    private string _acceptedText = "";
    private string _savedName = "";
    private bool _savedAutoConnect;

    public ConnectionDeviceView()
    {
        InitializeComponent();
        renameTextBox.TextChanged += OnRenameTextChanged;
        autoConnectToggle.CheckedChanged += (_, _) => { if (!Updating) { DraftChanged(); } };
        renameTextBox.KeyDown += (_, args) =>
        {
            if (args.KeyCode != Keys.Enter) { return; }
            args.SuppressKeyPress = true;
            if (saveButton.Enabled) { saveButton.PerformClick(); }
        };
    }

    internal string? DeviceKey { get; private set; }
    internal bool Updating { get; private set; }
    internal bool IsHistory { get; private set; }
    internal bool HasChanges => renameTextBox.Text != _savedName || autoConnectToggle.Checked != _savedAutoConnect;
    internal ModernToggle AutoConnectToggle => autoConnectToggle;
    internal ModernButton SaveButton => saveButton;
    internal ModernButton ConnectButton => connectButton;
    internal ModernButton ForgetButton => forgetButton;
    internal ModernButton CancelButton => cancelButton;
    internal TextBox RenameTextBox => renameTextBox;
    internal Label Status => status;

    internal void OpenDevice(ManagedAndroidDevice device, bool wirelessMethod, bool history)
    {
        DeviceKey = null;
        SetDevice(device, wirelessMethod, history);
    }

    internal void CancelEdit()
    {
        Updating = true;
        try
        {
            renameTextBox.Text = _acceptedText = _savedName;
            autoConnectToggle.Checked = _savedAutoConnect;
            status.Text = "";
        }
        finally { Updating = false; }
        SetBusy(_busy);
    }

    internal void SetDevice(ManagedAndroidDevice device, bool wirelessMethod, bool history)
    {
        bool changed = DeviceKey != device.DeviceKey;
        bool preserveDraft = !changed && HasChanges;
        Updating = true;
        try
        {
            DeviceKey = device.DeviceKey;
            IsHistory = history;
            title.Text = device.DisplayName;
            details.Text = AndroidDisplayNames.Transport(device.Transport) + " · " +
                (device.IsSelected ? "当前连接" : device.IsAvailable ? AndroidDisplayNames.DeviceStatus(device.Status) : "当前未识别") +
                (history ? "\n最近连接：" + device.LastConnectedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "");
            _savedName = device.CustomName ?? "";
            _savedAutoConnect = device.AutoConnect;
            if (!preserveDraft)
            {
                renameTextBox.Text = _acceptedText = _savedName;
                autoConnectToggle.Checked = _savedAutoConnect;
            }
            renameTextBox.PlaceholderText = device.DisplayName;
            autoConnectToggle.Text = autoConnectToggle.Checked ? "开启" : "关闭";
            connectButton.Visible = !history && !device.IsSelected;
            forgetButton.Visible = history;
            saveButton.Left = UiLayoutMetrics.Pixels(this, history || !device.IsSelected ? 98 : 196);
            _canConnect = device.IsAvailable && !device.IsSelected && device.Status == AndroidDeviceStatus.Ready &&
                (wirelessMethod == (device.Transport == AndroidTransport.Network));
            if (changed) { status.Text = ""; status.ForeColor = UiPalette.TextSecondary; }
            SetBusy(_busy);
        }
        finally { Updating = false; }
    }

    private void OnRenameTextChanged(object? sender, EventArgs args)
    {
        if (Updating) { return; }
        string candidate = renameTextBox.Text;
        if (candidate.Length > 0 && !DeviceNamePolicy.IsValid(candidate))
        {
            Updating = true;
            try { renameTextBox.Text = _acceptedText; renameTextBox.SelectionStart = _acceptedText.Length; }
            finally { Updating = false; }
            status.Text = "仅限汉字、英文字母、数字，最多 20 字";
            status.ForeColor = UiPalette.Warning;
            return;
        }
        _acceptedText = candidate;
        DraftChanged();
    }

    private void DraftChanged()
    {
        autoConnectToggle.Text = autoConnectToggle.Checked ? "开启" : "关闭";
        status.Text = HasChanges ? "配置待保存" : "";
        status.ForeColor = UiPalette.TextSecondary;
        SetBusy(_busy);
    }

    internal bool TryGetDraft(out DevicePreferenceDraft? draft)
    {
        draft = null;
        bool changedName = renameTextBox.Text != _savedName;
        if (changedName && !DeviceNamePolicy.IsValid(renameTextBox.Text))
        {
            status.Text = "名称不能为空，限 1–20 个汉字、英文字母或数字";
            status.ForeColor = UiPalette.Warning;
            return false;
        }
        if (DeviceKey is not { } key) { return false; }
        draft = new(key, renameTextBox.Text.Length == 0 ? null : renameTextBox.Text, autoConnectToggle.Checked);
        return true;
    }

    internal void SaveCompleted(DevicePreferenceDraft draft)
    {
        if (DeviceKey != draft.DeviceKey) { return; }
        _savedName = draft.CustomName ?? "";
        _savedAutoConnect = draft.AutoConnect;
        status.Text = HasChanges ? "配置待保存" : "配置已保存";
        status.ForeColor = HasChanges ? UiPalette.TextSecondary : UiPalette.Success;
        SetBusy(_busy);
    }

    internal void SetBusy(bool busy)
    {
        _busy = busy;
        bool valid = renameTextBox.Text == _savedName || DeviceNamePolicy.IsValid(renameTextBox.Text);
        autoConnectToggle.Enabled = !busy;
        renameTextBox.ReadOnly = busy;
        saveButton.Enabled = !busy && HasChanges && valid;
        connectButton.Enabled = _canConnect && !busy && valid;
        forgetButton.Enabled = !busy;
        cancelButton.Enabled = !busy;
    }
}
