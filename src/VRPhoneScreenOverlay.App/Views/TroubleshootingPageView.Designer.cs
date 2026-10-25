namespace VRPhoneScreenOverlay.App.Views;

partial class TroubleshootingPageView
{
    private System.ComponentModel.IContainer components = null!;
    private Label titleLabel = null!;
    private Label descriptionLabel = null!;
    private Label statusLabel = null!;
    private RichTextBox progressSummary = null!;
    private ModernButton cancelButton = null!;
    private ModernButton refreshButton = null!;
    private DataGridView history = null!;
    private DataGridViewTextBoxColumn timeColumn = null!;
    private DataGridViewTextBoxColumn summaryColumn = null!;
    private DataGridViewTextBoxColumn resultColumn = null!;
    private ModernButton fixedButton = null!;
    private ModernButton notFixedButton = null!;
    private ModernButton reproduceButton = null!;
    private ModernButton skipReproductionButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        titleLabel = new Label();
        descriptionLabel = new Label();
        statusLabel = new Label();
        progressSummary = new RichTextBox();
        cancelButton = new ModernButton();
        refreshButton = new ModernButton();
        history = new DataGridView();
        timeColumn = new DataGridViewTextBoxColumn();
        summaryColumn = new DataGridViewTextBoxColumn();
        resultColumn = new DataGridViewTextBoxColumn();
        fixedButton = new ModernButton();
        notFixedButton = new ModernButton();
        reproduceButton = new ModernButton();
        skipReproductionButton = new ModernButton();
        ((System.ComponentModel.ISupportInitialize)history).BeginInit();
        SuspendLayout();
        // titleLabel
        titleLabel.Location = new Point(16, 12);
        titleLabel.Name = "titleLabel";
        titleLabel.Size = new Size(560, 28);
        titleLabel.Text = "DeepSeek故障诊断";
        titleLabel.ForeColor = UiPalette.TextPrimary;
        // descriptionLabel
        descriptionLabel.Location = new Point(16, 43);
        descriptionLabel.Name = "descriptionLabel";
        descriptionLabel.Size = new Size(560, 22);
        descriptionLabel.Text = "查看AI的调查过程和建议，并反馈问题是否解决。";
        descriptionLabel.ForeColor = UiPalette.TextSecondary;
        // cancelButton
        cancelButton.Location = new Point(16, 72);
        cancelButton.Name = "cancelButton";
        cancelButton.Size = new Size(100, 38);
        cancelButton.Text = "返回";
        // refreshButton
        refreshButton.Location = new Point(444, 72);
        refreshButton.Name = "refreshButton";
        refreshButton.Size = new Size(132, 38);
        refreshButton.Text = "刷新历史记录";
        // statusLabel
        statusLabel.Location = new Point(16, 114);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(560, 24);
        statusLabel.Text = "新诊断请从上传问题诊断包页面发起。";
        statusLabel.ForeColor = UiPalette.TextSecondary;
        // progressSummary
        progressSummary.Name = "progressSummary";
        progressSummary.Location = new Point(16, 146);
        progressSummary.Size = new Size(560, 136);
        progressSummary.Multiline = true;
        progressSummary.ReadOnly = true;
        progressSummary.ScrollBars = RichTextBoxScrollBars.Vertical;
        progressSummary.DetectUrls = false;
        progressSummary.EnableAutoDragDrop = false;
        progressSummary.BackColor = UiPalette.Surface;
        progressSummary.ForeColor = UiPalette.TextSecondary;
        progressSummary.BorderStyle = BorderStyle.None;
        progressSummary.Text = "选择一条历史记录查看详情。";
        // history
        history.Location = new Point(16, 296);
        history.Name = "history";
        history.Size = new Size(560, 112);
        history.AllowUserToAddRows = false;
        history.AllowUserToDeleteRows = false;
        history.AllowUserToResizeRows = false;
        history.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        history.RowTemplate.Height = 28;
        history.ShowCellToolTips = false;
        history.BackgroundColor = UiPalette.Surface;
        history.BorderStyle = BorderStyle.None;
        history.ReadOnly = true;
        history.RowHeadersVisible = false;
        history.MultiSelect = false;
        history.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        history.EnableHeadersVisualStyles = false;
        history.DefaultCellStyle.BackColor = UiPalette.Surface;
        history.DefaultCellStyle.ForeColor = UiPalette.TextPrimary;
        history.DefaultCellStyle.SelectionBackColor = UiPalette.LegacyButton;
        history.DefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
        history.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        history.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.Surface;
        history.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextSecondary;
        history.Columns.AddRange(timeColumn, summaryColumn, resultColumn);
        timeColumn.Name = "timeColumn";
        timeColumn.HeaderText = "时间";
        timeColumn.Width = 100;
        summaryColumn.Name = "summaryColumn";
        summaryColumn.HeaderText = "故障描述";
        summaryColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        resultColumn.Name = "resultColumn";
        resultColumn.HeaderText = "结果";
        resultColumn.Width = 180;
        fixedButton.Name = "fixedButton";
        fixedButton.Location = new Point(128, 72);
        fixedButton.Size = new Size(100, 38);
        fixedButton.Text = "已修复";
        fixedButton.Enabled = false;
        notFixedButton.Name = "notFixedButton";
        notFixedButton.Location = new Point(240, 72);
        notFixedButton.Size = new Size(100, 38);
        notFixedButton.Text = "没修好";
        notFixedButton.Enabled = false;
        reproduceButton.Name = "reproduceButton";
        reproduceButton.Location = new Point(128, 72);
        reproduceButton.Size = new Size(200, 38);
        reproduceButton.Text = "开始复现采集";
        reproduceButton.ForeColor = TroubleshootingTextColors.Action;
        reproduceButton.EmphasisColor = TroubleshootingTextColors.Action;
        reproduceButton.Visible = false;
        skipReproductionButton.Name = "skipReproductionButton";
        skipReproductionButton.Location = new Point(340, 72);
        skipReproductionButton.Size = new Size(92, 38);
        skipReproductionButton.Text = "跳过";
        skipReproductionButton.ForeColor = TroubleshootingTextColors.Skip;
        skipReproductionButton.EmphasisColor = TroubleshootingTextColors.Skip;
        skipReproductionButton.Visible = false;
        // TroubleshootingPageView
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiPalette.Surface;
        Controls.Add(titleLabel);
        Controls.Add(descriptionLabel);
        Controls.Add(cancelButton);
        Controls.Add(refreshButton);
        Controls.Add(statusLabel);
        Controls.Add(progressSummary);
        Controls.Add(history);
        Controls.Add(fixedButton);
        Controls.Add(notFixedButton);
        Controls.Add(reproduceButton);
        Controls.Add(skipReproductionButton);
        Name = "TroubleshootingPageView";
        Size = new Size(596, 424);
        ((System.ComponentModel.ISupportInitialize)history).EndInit();
        ResumeLayout(false);
    }
}
