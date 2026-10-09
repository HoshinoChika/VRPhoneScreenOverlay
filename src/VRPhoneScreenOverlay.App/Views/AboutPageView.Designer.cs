#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class AboutPageView
{
    private System.ComponentModel.IContainer components = null;
    private SurfacePanel updateCard = null!;
    private Label updateTitle = null!;
    private Label channelTitle = null!;
    private UpdateChannelSelectorControl updateChannelSelector = null!;
    private ModernButton checkUpdateButton = null!;
    private SurfacePanel diagnosticsCard = null!;
    private Label diagnosticsTitle = null!;
    private Label diagnosticsDescription = null!;
    private ModernButton uploadDiagnosticsButton = null!;
    private SurfacePanel productCard = null!;
    private Label productTitle = null!;
    private Label productDetails = null!;
    private LinkLabel repositoryLink = null!;
    private EmbeddedImageView repositoryIcon = null!;
    private Label sourceTitle = null!;
    private Label contactTitle = null!;
    private EmbeddedImageView authorCode = null!;
    private SurfacePanel enlargedCodePanel = null!;
    private EmbeddedImageView enlargedCode = null!;
    private ModernButton closeCodeButton = null!;
    private Label operationStatus = null!;
    private SurfacePanel diagnosticReportPanel = null!;
    private Label diagnosticReportTitle = null!;
    private Label diagnosticReportInstructions = null!;
    private Label issueTypeLabel = null!;
    private DiagnosticIssueSelectorControl issueTypeComboBox = null!;
    private Label occurredAtLabel = null!;
    private ModernDateTimeInput occurredAtPicker = null!;
    private Label issueDescriptionLabel = null!;
    private ModernMultilineInput issueDescriptionTextBox = null!;
    private Label descriptionCounter = null!;
    private ModernButton cancelDiagnosticReportButton = null!;
    private ModernButton confirmDiagnosticUploadButton = null!;

    private ContextHelpIcon updateTitleHelp;
    private ContextHelpIcon diagnosticsTitleHelp;


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        updateTitleHelp = new ContextHelpIcon();
        diagnosticsTitleHelp = new ContextHelpIcon();

        updateCard = new SurfacePanel();
        updateTitle = new Label();
        channelTitle = new Label();
        updateChannelSelector = new UpdateChannelSelectorControl();
        checkUpdateButton = new ModernButton();
        diagnosticsCard = new SurfacePanel();
        diagnosticsTitle = new Label();
        diagnosticsDescription = new Label();
        uploadDiagnosticsButton = new ModernButton();
        productCard = new SurfacePanel();
        productTitle = new Label();
        productDetails = new Label();
        repositoryLink = new LinkLabel();
        repositoryIcon = new EmbeddedImageView();
        sourceTitle = new Label();
        contactTitle = new Label();
        authorCode = new EmbeddedImageView();
        enlargedCodePanel = new SurfacePanel();
        enlargedCode = new EmbeddedImageView();
        closeCodeButton = new ModernButton();
        operationStatus = new Label();
        diagnosticReportPanel = new SurfacePanel();
        diagnosticReportTitle = new Label();
        diagnosticReportInstructions = new Label();
        issueTypeLabel = new Label();
        issueTypeComboBox = new DiagnosticIssueSelectorControl();
        occurredAtLabel = new Label();
        occurredAtPicker = new ModernDateTimeInput();
        issueDescriptionLabel = new Label();
        issueDescriptionTextBox = new ModernMultilineInput();
        descriptionCounter = new Label();
        cancelDiagnosticReportButton = new ModernButton();
        confirmDiagnosticUploadButton = new ModernButton();
        updateCard.SuspendLayout();
        diagnosticsCard.SuspendLayout();
        productCard.SuspendLayout();
        diagnosticReportPanel.SuspendLayout();
        SuspendLayout();
        // updateTitleHelp
        updateTitleHelp.Name = "updateTitleHelp";
        updateTitleHelp.Location = new Point(118, 12);
        updateTitleHelp.Size = new Size(22, 22);
        updateTitleHelp.HelpText = UiHelpContent.Update;
        updateTitleHelp.TargetLabel = updateTitle;
        updateTitleHelp.AccessibleName = "软件更新说明";
        // diagnosticsTitleHelp
        diagnosticsTitleHelp.Name = "diagnosticsTitleHelp";
        diagnosticsTitleHelp.Location = new Point(128, 12);
        diagnosticsTitleHelp.Size = new Size(22, 22);
        diagnosticsTitleHelp.HelpText = UiHelpContent.Diagnostics;
        diagnosticsTitleHelp.TargetLabel = diagnosticsTitle;
        diagnosticsTitleHelp.AccessibleName = "问题诊断包说明";

        // updateCard
        updateCard.BorderColor = UiPalette.BorderSoft;
        updateCard.Controls.Add(updateTitleHelp);
        updateCard.Controls.Add(updateTitle);
        updateCard.Controls.Add(channelTitle);
        updateCard.Controls.Add(updateChannelSelector);
        updateCard.Controls.Add(checkUpdateButton);
        updateCard.CornerRadius = 14;
        updateCard.Location = new Point(0, 0);
        updateCard.Name = "updateCard";
        updateCard.Size = new Size(596, 124);
        updateCard.SurfaceColor = UiPalette.Surface;
        // updateTitle
        updateTitle.AutoSize = false;
        updateTitle.BackColor = Color.Transparent;
        updateTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        updateTitle.ForeColor = UiPalette.TextPrimary;
        updateTitle.Location = new Point(16, 12);
        updateTitle.Name = "updateTitle";
        updateTitle.Size = new Size(94, 24);
        updateTitle.Text = "软件更新";
        // channelTitle
        channelTitle.AutoSize = false;
        channelTitle.BackColor = Color.Transparent;
        channelTitle.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        channelTitle.ForeColor = UiPalette.TextSecondary;
        channelTitle.Location = new Point(16, 48);
        channelTitle.Name = "channelTitle";
        channelTitle.Size = new Size(88, 28);
        channelTitle.TextAlign = ContentAlignment.MiddleLeft;
        channelTitle.Text = "更新通道";
        // updateChannelSelector
        updateChannelSelector.Location = new Point(108, 48);
        updateChannelSelector.Name = "updateChannelSelector";
        updateChannelSelector.Size = new Size(76, 28);
        // checkUpdateButton
        checkUpdateButton.Icon = UiIcons.Download;
        checkUpdateButton.Location = new Point(448, 44);
        checkUpdateButton.Name = "checkUpdateButton";
        checkUpdateButton.Size = new Size(132, 38);
        checkUpdateButton.Text = "检查更新";
        // diagnosticsCard
        diagnosticsCard.BorderColor = UiPalette.BorderSoft;
        diagnosticsCard.Controls.Add(diagnosticsTitleHelp);
        diagnosticsCard.Controls.Add(diagnosticsTitle);
        diagnosticsCard.Controls.Add(diagnosticsDescription);
        diagnosticsCard.Controls.Add(uploadDiagnosticsButton);
        diagnosticsCard.CornerRadius = 14;
        diagnosticsCard.Location = new Point(0, 132);
        diagnosticsCard.Name = "diagnosticsCard";
        diagnosticsCard.Size = new Size(596, 76);
        diagnosticsCard.SurfaceColor = UiPalette.Surface;
        // diagnosticsTitle
        diagnosticsTitle.AutoSize = false;
        diagnosticsTitle.BackColor = Color.Transparent;
        diagnosticsTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        diagnosticsTitle.ForeColor = UiPalette.TextPrimary;
        diagnosticsTitle.Location = new Point(16, 12);
        diagnosticsTitle.Name = "diagnosticsTitle";
        diagnosticsTitle.Size = new Size(102, 24);
        diagnosticsTitle.Text = "问题诊断包";
        // diagnosticsDescription
        diagnosticsDescription.AutoSize = false;
        diagnosticsDescription.BackColor = Color.Transparent;
        diagnosticsDescription.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        diagnosticsDescription.ForeColor = UiPalette.TextSecondary;
        diagnosticsDescription.Location = new Point(16, 42);
        diagnosticsDescription.Name = "diagnosticsDescription";
        diagnosticsDescription.Size = new Size(380, 28);
        diagnosticsDescription.Text = "运行状态与脱敏日志";
        // uploadDiagnosticsButton
        uploadDiagnosticsButton.Icon = UiIcons.Upload;
        uploadDiagnosticsButton.Location = new Point(448, 20);
        uploadDiagnosticsButton.Name = "uploadDiagnosticsButton";
        uploadDiagnosticsButton.Size = new Size(132, 38);
        uploadDiagnosticsButton.Text = "填写并上传";
        // productCard
        productCard.BorderColor = UiPalette.BorderSoft;
        productCard.Controls.Add(productTitle);
        productCard.Controls.Add(productDetails);
        productCard.CornerRadius = 14;
        productCard.Location = new Point(0, 216);
        productCard.Name = "productCard";
        productCard.Size = new Size(596, 208);
        productCard.SurfaceColor = UiPalette.Surface;
        // productTitle
        productTitle.AutoSize = false;
        productTitle.BackColor = Color.Transparent;
        productTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
        productTitle.ForeColor = UiPalette.TextPrimary;
        productTitle.Location = new Point(16, 14);
        productTitle.Name = "productTitle";
        productTitle.Size = new Size(360, 24);
        productTitle.Text = "VRPhoneScreen Overlay";
        // productDetails
        productDetails.AutoSize = false;
        productDetails.BackColor = Color.Transparent;
        productDetails.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        productDetails.ForeColor = UiPalette.TextSecondary;
        productDetails.Location = new Point(16, 40);
        productDetails.Name = "productDetails";
        productDetails.Size = new Size(380, 26);
        productDetails.Text = "版本";
        // operationStatus
        operationStatus.AutoSize = false;
        operationStatus.BackColor = Color.Transparent;
        operationStatus.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        operationStatus.ForeColor = UiPalette.TextSecondary;
        operationStatus.Location = new Point(16, 92);
        operationStatus.Name = "operationStatus";
        operationStatus.Size = new Size(564, 22);
        operationStatus.Text = "";
        operationStatus.TextAlign = ContentAlignment.MiddleLeft;
        // diagnosticReportPanel
        diagnosticReportPanel.BorderColor = UiPalette.BorderSoft;
        diagnosticReportPanel.Controls.Add(diagnosticReportTitle);
        diagnosticReportPanel.Controls.Add(diagnosticReportInstructions);
        diagnosticReportPanel.Controls.Add(issueTypeLabel);
        diagnosticReportPanel.Controls.Add(issueTypeComboBox);
        diagnosticReportPanel.Controls.Add(occurredAtLabel);
        diagnosticReportPanel.Controls.Add(occurredAtPicker);
        diagnosticReportPanel.Controls.Add(issueDescriptionLabel);
        diagnosticReportPanel.Controls.Add(issueDescriptionTextBox);
        diagnosticReportPanel.Controls.Add(descriptionCounter);
        diagnosticReportPanel.Controls.Add(cancelDiagnosticReportButton);
        diagnosticReportPanel.Controls.Add(confirmDiagnosticUploadButton);
        diagnosticReportPanel.CornerRadius = 14;
        diagnosticReportPanel.Location = new Point(0, 0);
        diagnosticReportPanel.Name = "diagnosticReportPanel";
        diagnosticReportPanel.Size = new Size(596, 374);
        diagnosticReportPanel.SurfaceColor = UiPalette.Surface;
        diagnosticReportPanel.Visible = false;
        // diagnosticReportTitle
        diagnosticReportTitle.AutoSize = false;
        diagnosticReportTitle.BackColor = Color.Transparent;
        diagnosticReportTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
        diagnosticReportTitle.ForeColor = UiPalette.TextPrimary;
        diagnosticReportTitle.Location = new Point(20, 14);
        diagnosticReportTitle.Name = "diagnosticReportTitle";
        diagnosticReportTitle.Size = new Size(240, 28);
        diagnosticReportTitle.Text = "上传问题诊断包";
        // diagnosticReportInstructions
        diagnosticReportInstructions.AutoSize = false;
        diagnosticReportInstructions.BackColor = Color.Transparent;
        diagnosticReportInstructions.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        diagnosticReportInstructions.ForeColor = UiPalette.TextSecondary;
        diagnosticReportInstructions.Location = new Point(20, 47);
        diagnosticReportInstructions.Name = "diagnosticReportInstructions";
        diagnosticReportInstructions.Size = new Size(556, 24);
        diagnosticReportInstructions.Text = "";
        // issueTypeLabel
        issueTypeLabel.AutoSize = false;
        issueTypeLabel.BackColor = Color.Transparent;
        issueTypeLabel.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        issueTypeLabel.ForeColor = UiPalette.TextSecondary;
        issueTypeLabel.Location = new Point(20, 88);
        issueTypeLabel.Name = "issueTypeLabel";
        issueTypeLabel.Size = new Size(90, 32);
        issueTypeLabel.Text = "问题类型";
        issueTypeLabel.TextAlign = ContentAlignment.MiddleLeft;
        // issueTypeComboBox
        issueTypeComboBox.Location = new Point(120, 88);
        issueTypeComboBox.Name = "issueTypeComboBox";
        issueTypeComboBox.Size = new Size(456, 38);
        // occurredAtLabel
        occurredAtLabel.AutoSize = false;
        occurredAtLabel.BackColor = Color.Transparent;
        occurredAtLabel.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        occurredAtLabel.ForeColor = UiPalette.TextSecondary;
        occurredAtLabel.Location = new Point(20, 132);
        occurredAtLabel.Name = "occurredAtLabel";
        occurredAtLabel.Size = new Size(90, 32);
        occurredAtLabel.Text = "发生时间";
        occurredAtLabel.TextAlign = ContentAlignment.MiddleLeft;
        // occurredAtPicker
        occurredAtPicker.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        occurredAtPicker.ForeColor = UiPalette.TextPrimary;
        occurredAtPicker.Location = new Point(120, 132);
        occurredAtPicker.Name = "occurredAtPicker";
        occurredAtPicker.Size = new Size(456, 32);
        // issueDescriptionLabel
        issueDescriptionLabel.AutoSize = false;
        issueDescriptionLabel.BackColor = Color.Transparent;
        issueDescriptionLabel.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        issueDescriptionLabel.ForeColor = UiPalette.TextSecondary;
        issueDescriptionLabel.Location = new Point(20, 176);
        issueDescriptionLabel.Name = "issueDescriptionLabel";
        issueDescriptionLabel.Size = new Size(90, 32);
        issueDescriptionLabel.Text = "现象描述";
        issueDescriptionLabel.TextAlign = ContentAlignment.MiddleLeft;
        // issueDescriptionTextBox
        issueDescriptionTextBox.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        issueDescriptionTextBox.ForeColor = UiPalette.TextPrimary;
        issueDescriptionTextBox.Location = new Point(120, 176);
        issueDescriptionTextBox.MaxLength = UiInputPolicy.MaximumDescriptionLength;
        issueDescriptionTextBox.Name = "issueDescriptionTextBox";
        issueDescriptionTextBox.PlaceholderText = "可选，最多 300 字，例如：旋转后画面冻结";
        issueDescriptionTextBox.Size = new Size(456, 94);
        // descriptionCounter
        descriptionCounter.AutoSize = false;
        descriptionCounter.BackColor = Color.Transparent;
        descriptionCounter.Font = new Font("Segoe UI", 7F, FontStyle.Regular, GraphicsUnit.Point);
        descriptionCounter.ForeColor = UiPalette.TextMuted;
        descriptionCounter.Location = new Point(492, 276);
        descriptionCounter.Name = "descriptionCounter";
        descriptionCounter.Size = new Size(84, 20);
        descriptionCounter.Text = "0 / 300";
        descriptionCounter.TextAlign = ContentAlignment.MiddleRight;
        descriptionCounter.UseMnemonic = false;
        // cancelDiagnosticReportButton
        cancelDiagnosticReportButton.Icon = UiIcons.Close;
        cancelDiagnosticReportButton.Location = new Point(344, 318);
        cancelDiagnosticReportButton.Name = "cancelDiagnosticReportButton";
        cancelDiagnosticReportButton.Size = new Size(108, 40);
        cancelDiagnosticReportButton.Text = "取消";
        // confirmDiagnosticUploadButton
        confirmDiagnosticUploadButton.Enabled = false;
        confirmDiagnosticUploadButton.Icon = UiIcons.Upload;
        confirmDiagnosticUploadButton.Location = new Point(464, 318);
        confirmDiagnosticUploadButton.Name = "confirmDiagnosticUploadButton";
        confirmDiagnosticUploadButton.Size = new Size(112, 40);
        confirmDiagnosticUploadButton.Text = "开始上传";
        // AboutPageView
        AutoScaleMode = AutoScaleMode.None;
        AutoSize = false;
        BackColor = UiPalette.Window;
        Controls.Add(updateCard);
        Controls.Add(diagnosticsCard);
        Controls.Add(productCard);
        updateCard.Controls.Add(operationStatus);
        Controls.Add(diagnosticReportPanel);
        // Project and contact information
        closeCodeButton.Name = "closeCodeButton";
        enlargedCode.Name = "enlargedCode";
        enlargedCodePanel.Name = "enlargedCodePanel";
        authorCode.Name = "authorCode";
        contactTitle.Name = "contactTitle";
        sourceTitle.Name = "sourceTitle";
        repositoryIcon.Name = "repositoryIcon";
        repositoryLink.Name = "repositoryLink";
        repositoryIcon.ResourceName = "github-mark.png";
        repositoryIcon.Location = new Point(16, 78);
        repositoryIcon.Size = new Size(20, 20);
        sourceTitle.Text = "开源地址：";
        sourceTitle.Location = new Point(40, 76);
        sourceTitle.TextAlign = ContentAlignment.MiddleLeft;
        sourceTitle.Size = new Size(78, 24);
        sourceTitle.ForeColor = UiPalette.TextSecondary;
        repositoryLink.Text = "VRPhoneScreenOverlay";
        repositoryLink.Location = new Point(118, 76);
        repositoryLink.TextAlign = ContentAlignment.MiddleLeft;
        repositoryLink.Size = new Size(280, 24);
        repositoryLink.LinkColor = UiPalette.TextPrimary;
        repositoryLink.ActiveLinkColor = UiPalette.Accent;
        repositoryLink.VisitedLinkColor = UiPalette.TextPrimary;
        contactTitle.Text = "联系作者（打开抖音扫一扫）";
        contactTitle.ForeColor = UiPalette.TextSecondary;
        contactTitle.Location = new Point(382, 174);
        contactTitle.Size = new Size(208, 24);
        contactTitle.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        contactTitle.TextAlign = ContentAlignment.MiddleCenter;
        authorCode.ResourceName = "author-douyin.jpg";
        authorCode.Crop = new Rectangle(66, 0, 1204, 1204);
        authorCode.Location = new Point(408, 12);
        authorCode.Size = new Size(156, 156);
        authorCode.Cursor = Cursors.Hand;
        authorCode.AccessibleName = "联系作者，点击放大抖音二维码";
        productCard.Controls.Add(repositoryIcon);
        productCard.Controls.Add(sourceTitle);
        productCard.Controls.Add(repositoryLink);
        productCard.Controls.Add(contactTitle);
        productCard.Controls.Add(authorCode);
        enlargedCodePanel.Location = new Point(0, 0);
        enlargedCodePanel.Size = new Size(596, 424);
        enlargedCodePanel.SurfaceColor = UiPalette.Surface;
        enlargedCodePanel.Visible = false;
        enlargedCode.ResourceName = "author-douyin.jpg";
        enlargedCode.Crop = new Rectangle(66, 0, 1204, 1204);
        enlargedCode.Location = new Point(132, 14);
        enlargedCode.Size = new Size(332, 332);
        closeCodeButton.Text = "返回关于";
        closeCodeButton.Location = new Point(228, 366);
        closeCodeButton.Size = new Size(140, 40);
        enlargedCodePanel.Controls.Add(enlargedCode);
        enlargedCodePanel.Controls.Add(closeCodeButton);
        Controls.Add(enlargedCodePanel);
        Name = "AboutPageView";
        Size = new Size(596, 424);
        updateCard.ResumeLayout(false);
        diagnosticsCard.ResumeLayout(false);
        productCard.ResumeLayout(false);
        diagnosticReportPanel.ResumeLayout(false);
        diagnosticReportPanel.PerformLayout();
        ResumeLayout(false);
    }
}
