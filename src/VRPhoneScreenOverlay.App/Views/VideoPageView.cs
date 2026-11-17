namespace VRPhoneScreenOverlay.App.Views;

public partial class VideoPageView : UserControl
{
    public VideoPageView() { InitializeComponent(); }
    internal VideoUploadModeSelectorControl UploadSelector => uploadSelector;
    internal void ConfigureVideoUpload(VRPhoneScreenOverlay.Session.VideoUploadPreferences preferences) => ShowUploadOverride(preferences.Selection);
    internal void ShowUploadOverride(VRPhoneScreenOverlay.Contracts.VideoUploadSelection selected)
    {
        uploadOverride.Text = !selected.IsValid ? "诊断启动参数无效" : selected.Source == "environment"
            ? "本次诊断指定：" + (selected.RequestedMode switch { "staging" => "缓冲中转", "direct" => "直接提交", _ => "自动（推荐）" }) : string.Empty;
    }
    internal VideoResolutionSelectorControl ResolutionSelector => resolutionSelector;
    internal VideoBitrateSelectorControl BitrateSelector => bitrateSelector;
    internal VideoFrameRateSelectorControl FrameRateSelector => frameRateSelector;
    internal ModernButton DiscardButton => discardButton;
    internal ModernButton ApplyButton => applyButton;
    internal Label QualityStatus => qualityStatus;
    internal Label Status => status;
}
