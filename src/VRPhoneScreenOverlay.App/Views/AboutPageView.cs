namespace VRPhoneScreenOverlay.App.Views;

public partial class AboutPageView : UserControl
{
    private OutsideInputCommit? _dateCommit;

    public AboutPageView()
    {
        InitializeComponent();
        aboutScrollBar.Bind(aboutModules);
        OnAboutViewportSizeChanged(this, EventArgs.Empty);
        Load += (_, _) =>
        {
            if (_dateCommit is null && FindForm() is { } form)
            { _dateCommit = new OutsideInputCommit(form, occurredAtPicker, occurredAtPicker.CommitText); }
        };
        Disposed += (_, _) => { _dateCommit?.Dispose(); _dateCommit = null; };
        issueDescriptionTextBox.TextChanged += OnDescriptionTextChanged;
        authorCode.Click += (_, _) => ShowCode(authorCode, "联系作者 · 抖音扫一扫");
        groupCode.Click += (_, _) => ShowCode(groupCode, "VRPSO测试 · 群号：1075716261");
        closeCodeButton.Click += (_, _) => ShowMainContent();
        repositoryLink.LinkClicked += (_, _) =>
        {
            if (!VRPhoneScreenOverlay.Network.PublicProjectLink.TryOpen())
            { contactTitle.Text = "无法打开浏览器，请稍后重试"; }
        };
        UpdateDescriptionCounter();
    }

    private void ShowCode(EmbeddedImageView source, string title)
    {
        enlargedCode.ResourceName = source.ResourceName;
        enlargedCode.Crop = source.Crop;
        enlargedTitle.Text = title;
        aboutViewport.Visible = aboutScrollBar.Visible = false;
        enlargedCodePanel.Visible = true;
        enlargedCodePanel.BringToFront();
    }
    private bool _arranging;
    private float _layoutScale = 1;
    internal void ApplyScrollLayout(float scale)
    {
        if (_arranging) { return; }
        _arranging = true;
        _layoutScale = scale;
        try
        {
            int Px(int value) => (int)Math.Round(value * scale);
            int barWidth = Px(22);
            aboutScrollBar.SetBounds(ClientSize.Width - barWidth, 0, barWidth, ClientSize.Height);
            aboutViewport.SetBounds(0, 0, ClientSize.Width - barWidth, ClientSize.Height);
            aboutModules.SetBounds(0, 0, aboutViewport.ClientSize.Width + SystemInformation.VerticalScrollBarWidth,
                aboutViewport.ClientSize.Height);
            Region? previousClip = aboutModules.Region;
            aboutModules.Region = new Region(new Rectangle(0, 0, aboutViewport.ClientSize.Width, aboutViewport.ClientSize.Height));
            previousClip?.Dispose();
            int width = Math.Max(Px(320), aboutViewport.ClientSize.Width - Px(4));
            foreach (Control card in aboutModules.Controls) { card.Width = width; }
            diagnosticsDescription.Width = Math.Max(1, openDiagnosticLogsButton.Left - diagnosticsDescription.Left - Px(12));
            operationStatus.Width = Math.Max(1, width - operationStatus.Left - Px(16));
            int column = width / 2;
            int size = Math.Min(Px(210), column - Px(32));
            authorCode.SetBounds((column - size) / 2, Px(112), size, size);
            groupCode.SetBounds(column + (column - size) / 2, Px(112), size, size);
            contactTitle.SetBounds(Px(8), Px(112) + size + Px(12), column - Px(16), Px(48));
            groupTitle.SetBounds(column + Px(8), Px(112) + size + Px(12), column - Px(16), Px(48));
        }
        finally { _arranging = false; }
    }
    private void OnAboutViewportSizeChanged(object? sender, EventArgs e) => ApplyScrollLayout(_layoutScale);
    private void OnAboutModulesSizeChanged(object? sender, EventArgs e) => ApplyScrollLayout(_layoutScale);

    internal ModernButton ConfirmMemoryUploadButton => confirmMemoryUploadButton;
    internal ModernButton CancelMemoryUploadButton => cancelMemoryUploadButton;
    internal bool IncludeMemory => includeMemoryToggle.Checked;
    internal SurfacePanel MemoryConsentPanel => memoryConsentPanel;
    internal void ShowMemoryConsent()
    {
        includeMemoryToggle.Checked = false;
        memoryConsentPanel.Visible = true;
        memoryConsentPanel.BringToFront();
        confirmMemoryUploadButton.Focus();
    }
    internal void HideMemoryConsent() => memoryConsentPanel.Visible = false;

    internal ModernButton ExportCaptureButton => exportCaptureButton;
    internal ModernButton CaptureButton => captureButton;
    internal Label CaptureStatus => captureStatus;
    internal void SetCaptureStatus(string status)
    {
        captureButton.Text = CapturePresentation.IsActive(status) ? "停止自动记录" : "启用自动记录";
        exportCaptureButton.Text = CapturePresentation.IsActive(status) ? "停止并导出资料" : "导出深入资料";
        captureStatus.Text = CapturePresentation.Message(status);
    }

    internal UpdateChannelSelectorControl UpdateChannelSelector => updateChannelSelector;
    internal ModernButton CheckUpdateButton => checkUpdateButton;
    internal ModernButton UploadDiagnosticsButton => uploadDiagnosticsButton;
    internal ModernButton OpenDiagnosticLogsButton => openDiagnosticLogsButton;
    internal ModernButton ConfirmDiagnosticUploadButton => confirmDiagnosticUploadButton;
    internal ModernButton CancelDiagnosticReportButton => cancelDiagnosticReportButton;
    internal DiagnosticIssueSelectorControl IssueTypeComboBox => issueTypeComboBox;
    internal ModernDateTimeInput OccurredAtPicker => occurredAtPicker;
    internal ModernMultilineInput IssueDescriptionTextBox => issueDescriptionTextBox;
    internal SurfacePanel DiagnosticReportPanel => diagnosticReportPanel;
    internal Label DiagnosticsStatus => diagnosticsDescription;
    internal Label DiagnosticReportStatus => diagnosticReportInstructions;
    internal Label OperationStatus => operationStatus;
    internal void SetProductDetails(string value) => productDetails.Text = value;

    internal void ShowDiagnosticReport()
    {
        diagnosticReportInstructions.Text = string.Empty;
        DateTime now = DateTime.Now;
        occurredAtPicker.MinDate = now.AddDays(-7);
        occurredAtPicker.MaxDate = now;
        occurredAtPicker.Value = now;
        SetDiagnosticReportVisible(true);
    }

    internal void HideDiagnosticReport()
    {
        SetDiagnosticReportVisible(false);
    }

    internal void SetDiagnosticReportVisible(bool visible)
    {
        aboutViewport.Visible = aboutScrollBar.Visible = !visible;
        updateCard.Visible = !visible;
        diagnosticsCard.Visible = !visible;
        productCard.Visible = !visible;
        diagnosticReportPanel.Visible = visible;
        if (visible) { diagnosticReportPanel.BringToFront(); }
    }

    internal void ShowMainContent()
    {
        HideDiagnosticReport();
        enlargedCodePanel.Visible = false;
    }

    private void OnDescriptionTextChanged(object? sender, EventArgs eventArgs) =>
        UpdateDescriptionCounter();

    private void UpdateDescriptionCounter()
    {
        descriptionCounter.Text =
            $"{issueDescriptionTextBox.TextLength} / {UiInputPolicy.MaximumDescriptionLength}";
    }
}
