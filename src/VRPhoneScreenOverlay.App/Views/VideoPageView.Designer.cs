#nullable disable

namespace VRPhoneScreenOverlay.App.Views;

partial class VideoPageView
{
    private System.ComponentModel.IContainer components = null;
    private SurfacePanel card;
    private Label title;
    private Label qualityStatus;
    private Label status;
    private ModernButton applyButton;
    private ModernButton discardButton;
    private Label resolutionTitle;
    private VideoResolutionSelectorControl resolutionSelector;
    private Label bitrateTitle;
    private VideoBitrateSelectorControl bitrateSelector;
    private Label frameRateTitle;
    private VideoFrameRateSelectorControl frameRateSelector;
    private ContextHelpIcon resolutionTitleHelp;
    private ContextHelpIcon bitrateTitleHelp;
    private ContextHelpIcon frameRateTitleHelp;
    private Label uploadTitle;
    private VideoUploadModeSelectorControl uploadSelector;
    private Label uploadHint;
    private Label uploadOverride;
    private Panel videoDivider;
    private ContextHelpIcon uploadTitleHelp;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        resolutionTitleHelp = new ContextHelpIcon();
        bitrateTitleHelp = new ContextHelpIcon();
        frameRateTitleHelp = new ContextHelpIcon();
        uploadTitle = new Label();
        uploadSelector = new VideoUploadModeSelectorControl();
        uploadHint = new Label();
        uploadOverride = new Label();
        videoDivider = new Panel();
        uploadTitleHelp = new ContextHelpIcon();

        card = new SurfacePanel();
        title = new Label();
        qualityStatus = new Label();
        status = new Label();
        applyButton = new ModernButton();
        discardButton = new ModernButton();
        resolutionTitle = new Label();
        resolutionSelector = new VideoResolutionSelectorControl();
        bitrateTitle = new Label();
        bitrateSelector = new VideoBitrateSelectorControl();
        frameRateTitle = new Label();
        frameRateSelector = new VideoFrameRateSelectorControl();
        card.SuspendLayout();
        SuspendLayout();
        // resolutionTitleHelp
        resolutionTitleHelp.Name = "resolutionTitleHelp";
        resolutionTitleHelp.Location = new Point(126, 62);
        resolutionTitleHelp.Size = new Size(22, 22);
        resolutionTitleHelp.HelpText = UiHelpContent.Resolution;
        resolutionTitleHelp.TargetLabel = resolutionTitle;
        resolutionTitleHelp.AccessibleName = "分辨率说明";
        // bitrateTitleHelp
        bitrateTitleHelp.Name = "bitrateTitleHelp";
        bitrateTitleHelp.Location = new Point(126, 114);
        bitrateTitleHelp.Size = new Size(22, 22);
        bitrateTitleHelp.HelpText = UiHelpContent.Bitrate;
        bitrateTitleHelp.TargetLabel = bitrateTitle;
        bitrateTitleHelp.AccessibleName = "码率说明";
        // frameRateTitleHelp
        frameRateTitleHelp.Name = "frameRateTitleHelp";
        frameRateTitleHelp.Location = new Point(126, 166);
        frameRateTitleHelp.Size = new Size(22, 22);
        frameRateTitleHelp.HelpText = UiHelpContent.FrameRate;
        frameRateTitleHelp.TargetLabel = frameRateTitle;
        frameRateTitleHelp.AccessibleName = "帧率说明";

        // card
        card.Location = new Point(0, 0);
        card.Size = new Size(596, 424);
        card.SurfaceColor = UiPalette.Surface;
        card.BorderColor = UiPalette.BorderSoft;
        card.CornerRadius = 14;
        card.Name = "card";
        // title
        title.Location = new Point(18, 14);
        title.Size = new Size(400, 30);
        title.Text = "画面";
        title.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        title.ForeColor = UiPalette.TextPrimary;
        title.BackColor = Color.Transparent;
        title.Name = "title";
        // resolutionTitle
        resolutionTitle.Location = new Point(18, 60);
        resolutionTitle.Size = new Size(100, 30);
        resolutionTitle.Text = "分辨率";
        resolutionTitle.ForeColor = UiPalette.TextPrimary;
        resolutionTitle.BackColor = Color.Transparent;
        resolutionTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        resolutionTitle.Name = "resolutionTitle";
        // resolutionSelector
        resolutionSelector.Location = new Point(158, 54);
        resolutionSelector.Size = new Size(422, 40);
        resolutionSelector.Name = "resolutionSelector";
        // bitrateTitle
        bitrateTitle.Location = new Point(18, 112);
        bitrateTitle.Size = new Size(100, 30);
        bitrateTitle.Text = "码率";
        bitrateTitle.ForeColor = UiPalette.TextPrimary;
        bitrateTitle.BackColor = Color.Transparent;
        bitrateTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        bitrateTitle.Name = "bitrateTitle";
        // bitrateSelector
        bitrateSelector.Location = new Point(158, 106);
        bitrateSelector.Size = new Size(422, 40);
        bitrateSelector.Name = "bitrateSelector";
        // frameRateTitle
        frameRateTitle.Location = new Point(18, 164);
        frameRateTitle.Size = new Size(100, 30);
        frameRateTitle.Text = "帧率";
        frameRateTitle.ForeColor = UiPalette.TextPrimary;
        frameRateTitle.BackColor = Color.Transparent;
        frameRateTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        frameRateTitle.Name = "frameRateTitle";
        // frameRateSelector
        frameRateSelector.Location = new Point(158, 158);
        frameRateSelector.Size = new Size(422, 40);
        frameRateSelector.Name = "frameRateSelector";
        // qualityStatus
        qualityStatus.Location = new Point(18, 375);
        qualityStatus.Size = new Size(342, 24);
        qualityStatus.ForeColor = UiPalette.TextSecondary;
        qualityStatus.BackColor = Color.Transparent;
        qualityStatus.Name = "qualityStatus";
        // status
        status.Location = new Point(18, 328);
        status.Size = new Size(560, 34);
        status.ForeColor = UiPalette.TextSecondary;
        status.BackColor = Color.Transparent;
        status.Name = "status";
        // discardButton
        discardButton.Location = new Point(374, 368);
        discardButton.Size = new Size(98, 38);
        discardButton.Text = "放弃";
        discardButton.Icon = UiIcons.Undo;
        discardButton.Name = "discardButton";
        // applyButton
        applyButton.Location = new Point(484, 368);
        applyButton.Size = new Size(96, 38);
        applyButton.Text = "应用";
        applyButton.Icon = UiIcons.CheckMark;
        applyButton.Primary = true;
        applyButton.Name = "applyButton";
        videoDivider.Name = "videoDivider";
        videoDivider.Location = new Point(18, 211);
        videoDivider.Size = new Size(560, 1);
        videoDivider.BackColor = UiPalette.BorderSoft;
        uploadTitle.Name = "uploadTitle";
        uploadTitle.Text = "显卡画面处理方式";
        uploadTitle.Location = new Point(18, 220);
        uploadTitle.Size = new Size(300, 28);
        uploadTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        uploadTitle.ForeColor = UiPalette.TextPrimary;
        uploadTitle.BackColor = Color.Transparent;
        uploadTitleHelp.Name = "uploadTitleHelp";
        uploadTitleHelp.Size = new Size(22, 22);
        uploadTitleHelp.HelpText = "调整手机画面送入显卡的方式。";
        uploadTitleHelp.TargetLabel = uploadTitle;
        uploadTitleHelp.AccessibleName = "显卡画面处理方式说明";
        uploadSelector.Name = "uploadSelector";
        uploadSelector.Location = new Point(18, 257);
        uploadSelector.Size = new Size(560, 38);
        uploadHint.Name = "uploadHint";
        uploadHint.Text = "出现浮窗闪退或者软件崩溃的时候可以尝试切换。";
        uploadHint.Location = new Point(18, 300);
        uploadHint.Size = new Size(560, 22);
        uploadHint.ForeColor = UiPalette.TextSecondary;
        uploadHint.BackColor = Color.Transparent;
        uploadOverride.Name = "uploadOverride";
        uploadOverride.Location = new Point(320, 222);
        uploadOverride.Size = new Size(260, 24);
        uploadOverride.TextAlign = ContentAlignment.MiddleRight;
        uploadOverride.ForeColor = UiPalette.Warning;
        uploadOverride.BackColor = Color.Transparent;
        card.Controls.Add(videoDivider);
        card.Controls.Add(uploadTitle);
        card.Controls.Add(uploadTitleHelp);
        card.Controls.Add(uploadSelector);
        card.Controls.Add(uploadHint);
        card.Controls.Add(uploadOverride);
        card.Controls.Add(resolutionTitleHelp);
        card.Controls.Add(bitrateTitleHelp);
        card.Controls.Add(frameRateTitleHelp);
        card.Controls.Add(title);
        card.Controls.Add(qualityStatus);
        card.Controls.Add(status);
        card.Controls.Add(discardButton);
        card.Controls.Add(applyButton);
        card.Controls.Add(resolutionTitle);
        card.Controls.Add(resolutionSelector);
        card.Controls.Add(bitrateTitle);
        card.Controls.Add(bitrateSelector);
        card.Controls.Add(frameRateTitle);
        card.Controls.Add(frameRateSelector);
        // VideoPageView
        AutoScaleMode = AutoScaleMode.None;
        BackColor = UiPalette.Window;
        Size = new Size(596, 424);
        Name = "VideoPageView";
        Controls.Add(card);
        card.ResumeLayout(false);
        ResumeLayout(false);
    }
}
