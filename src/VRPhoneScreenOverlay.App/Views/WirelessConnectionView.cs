namespace VRPhoneScreenOverlay.App.Views;

public partial class WirelessConnectionView : UserControl
{
    private IReadOnlyList<VRPhoneScreenOverlay.Android.ManagedAndroidDevice> _devices = [];
    public WirelessConnectionView()
    {
        InitializeComponent();
        deviceManager.CancelButton.Click += (_, _) => { deviceManager.CancelEdit(); HideDeviceManager(); };
        methodSelector.SelectedValueChanged += (_, _) => ApplyMethod();
        ApplyMethod();
        fallbackButton.Click += (_, _) => SelectMethod(WirelessPairingMethod.Manual);
        qrImage.Paint += (_, eventArgs) =>
        {
            if (qrImage.Image is null)
            {
                TextRenderer.DrawText(eventArgs.Graphics, "正在生成二维码", Font, qrImage.ClientRectangle,
                    UiPalette.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        };
    }

    internal event EventHandler? MethodChanged;
    internal WirelessPairingMethod Method => methodSelector.TryGetSelectedValue(out WirelessPairingMethod value)
        ? value : WirelessPairingMethod.Usb;
    internal void SelectMethod(WirelessPairingMethod method)
    {
        _ = methodSelector.SelectValue(method);
    }

    private void ApplyMethod()
    {
        qrPanel.Visible = Method == WirelessPairingMethod.Scan;
        codePanel.Visible = Method == WirelessPairingMethod.Code;
        manualPanel.Visible = Method == WirelessPairingMethod.Manual;
        usbPanel.Visible = Method == WirelessPairingMethod.Usb;
        ClearPairingCode();
        SetDevices(_devices);
        MethodChanged?.Invoke(this, EventArgs.Empty);
    }
    internal void ShowManualFallback(bool visible) => fallbackButton.Visible = visible;
    internal bool ManualFallbackVisible => fallbackButton.Visible;
    internal bool AddressInputsVisible => manualPanel.Visible;
    internal ModernButton AutoPairButton => autoPairButton;

    internal void ShowQr(string? payload)
    {
        Image? old = qrImage.Image;
        qrImage.Image = null;
        old?.Dispose();
        qrImage.BackColor = payload is null ? UiPalette.Surface : Color.White;
        qrImage.Invalidate();
        if (payload is null) { return; }
        using QRCoder.QRCodeData data = QRCoder.QRCodeGenerator.GenerateQrCode(payload, QRCoder.QRCodeGenerator.ECCLevel.M);
        using QRCoder.QRCode code = new(data);
        qrImage.Image = code.GetGraphic(6);
    }

    internal void SetQrWaiting()
    {
        methodSelector.Enabled = true;
        refreshButton.Enabled = true;
        deviceList.Enabled = true;
        historyDeviceList.Enabled = true;
        cancelButton.Visible = false;
    }

    internal ModernButton BackButton => backButton;
    internal ModernButton PairButton => pairButton;
    internal ModernButton ConnectButton => connectButton;
    internal ModernButton RefreshButton => refreshButton;
    internal ModernButton CancelButton => cancelButton;
    internal ConnectionDeviceView DeviceManager => deviceManager;
    internal Panel DeviceManagerHost => deviceManagerHost;
    internal ListBox HistoryDeviceList => historyDeviceList;
    internal bool UpdatingDevices { get; private set; }

    internal void SetManagementBusy(bool busy)
    {
        SetBusy(busy);
        cancelButton.Visible = false;
    }

    internal void ShowDeviceManager(VRPhoneScreenOverlay.Android.ManagedAndroidDevice device, bool history = false)
    {
        deviceManager.OpenDevice(device, Method != WirelessPairingMethod.Usb, history);
        deviceManagerHost.Visible = true;
        deviceManagerHost.BringToFront();
    }
    internal void HideDeviceManager() => deviceManagerHost.Visible = false;

    internal void SetDevices(IReadOnlyList<VRPhoneScreenOverlay.Android.ManagedAndroidDevice> devices)
    {
        _devices = devices;
        UpdatingDevices = true;
        deviceList.BeginUpdate();
        historyDeviceList.BeginUpdate();
        try
        {
            string? currentKey = (deviceList.SelectedItem as ConnectionDeviceListEntry)?.Device.DeviceKey;
            string? historyKey = (historyDeviceList.SelectedItem as ConnectionDeviceListEntry)?.Device.DeviceKey;
            deviceList.Items.Clear();
            historyDeviceList.Items.Clear();
            foreach (var device in devices.Where(device => HasConnectionPath(device) &&
                (Method == WirelessPairingMethod.Usb ? device.Transport == VRPhoneScreenOverlay.Android.AndroidTransport.Usb
                    : device.Transport == VRPhoneScreenOverlay.Android.AndroidTransport.Network)).OrderByDescending(device => device.IsSelected))
            {
                int index = deviceList.Items.Add(new ConnectionDeviceListEntry(device));
                if (device.DeviceKey == currentKey) { deviceList.SelectedIndex = index; }
            }
            foreach (var device in devices.Where(device => !HasConnectionPath(device) && device.LastConnectedAt is not null &&
                (Method == WirelessPairingMethod.Usb ? device.Transport == VRPhoneScreenOverlay.Android.AndroidTransport.Usb
                    : device.Transport == VRPhoneScreenOverlay.Android.AndroidTransport.Network)).OrderByDescending(device => device.LastConnectedAt))
            {
                int index = historyDeviceList.Items.Add(new ConnectionDeviceListEntry(device));
                if (device.DeviceKey == historyKey) { historyDeviceList.SelectedIndex = index; }
            }
            currentEmpty.Visible = deviceList.Items.Count == 0;
            historyEmpty.Visible = historyDeviceList.Items.Count == 0;
        }
        finally
        {
            deviceList.EndUpdate();
            historyDeviceList.EndUpdate();
            UpdatingDevices = false;
        }
    }
    internal ListBox DeviceList => deviceList;
    internal static bool ShouldOpenDevice(ConnectionDeviceList list, Point location) => list.TryHitDevice(location, out _);
    private static bool HasConnectionPath(VRPhoneScreenOverlay.Android.ManagedAndroidDevice device) => device.IsAvailable &&
        device.Status is VRPhoneScreenOverlay.Android.AndroidDeviceStatus.Ready or VRPhoneScreenOverlay.Android.AndroidDeviceStatus.Unauthorized;
    internal Label Status => status;
    internal string PairEndpoint => Method == WirelessPairingMethod.Code ? string.Empty : pairEndpoint.Text;
    internal string ConnectEndpoint => connectEndpoint.Text;
    internal string PairingCode => Method == WirelessPairingMethod.Code ? autoPairCode.Text : pairCode.Text;

    internal void ClearPairingCode() { pairCode.Clear(); autoPairCode.Clear(); }

    internal void SetBusy(bool busy)
    {
        methodSelector.Enabled = !busy;
        autoPairButton.Enabled = !busy;
        autoPairCode.Enabled = !busy;
        pairButton.Enabled = !busy;
        connectButton.Enabled = !busy;
        pairEndpoint.Enabled = !busy;
        connectEndpoint.Enabled = !busy;
        pairCode.Enabled = !busy;
        refreshButton.Enabled = !busy;
        deviceList.Enabled = !busy;
        historyDeviceList.Enabled = !busy;
        cancelButton.Enabled = busy;
        cancelButton.Visible = busy;
        deviceManager.SetBusy(busy);
    }
}
