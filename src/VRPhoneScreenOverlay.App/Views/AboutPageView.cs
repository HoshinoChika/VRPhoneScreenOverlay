namespace VRPhoneScreenOverlay.App.Views;

public partial class AboutPageView : UserControl
{
    public AboutPageView()
    {
        InitializeComponent();
        issueDescriptionTextBox.TextChanged += OnDescriptionTextChanged;
        authorCode.Click += (_, _) => { enlargedCodePanel.Visible = true; enlargedCodePanel.BringToFront(); };
        closeCodeButton.Click += (_, _) => enlargedCodePanel.Visible = false;
        repositoryLink.LinkClicked += (_, _) =>
        {
            if (!VRPhoneScreenOverlay.Network.PublicProjectLink.TryOpen())
            { contactTitle.Text = "无法打开浏览器，请稍后重试"; }
        };
        UpdateDescriptionCounter();
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
    internal TroubleshootingPageView TroubleshootingPage => troubleshootingPage;
    internal Button DeepSeekDiagnosticsButton => deepSeekDiagnosticsButton;
    internal Button ViewAiHistoryButton => viewAiHistoryButton;

    internal void ShowTroubleshooting()
    {
        HideDiagnosticReport();
        updateCard.Visible = false;
        diagnosticsCard.Visible = false;
        productCard.Visible = false;
        operationStatus.Visible = false;
        troubleshootingPage.Visible = true;
        troubleshootingPage.BringToFront();
    }

    internal void HideTroubleshooting()
    {
        troubleshootingPage.Visible = false;
        operationStatus.Visible = true;
        SetDiagnosticReportVisible(false);
    }

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
        updateCard.Visible = !visible;
        diagnosticsCard.Visible = !visible;
        productCard.Visible = !visible;
        diagnosticReportPanel.Visible = visible;
        if (visible) { diagnosticReportPanel.BringToFront(); }
    }

    internal void ShowMainContent()
    {
        if (troubleshootingPage.IsBusy) { ShowTroubleshooting(); return; }
        HideTroubleshooting();
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
