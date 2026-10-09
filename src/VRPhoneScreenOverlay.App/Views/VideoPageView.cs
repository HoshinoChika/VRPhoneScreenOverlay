namespace VRPhoneScreenOverlay.App.Views;

public partial class VideoPageView : UserControl
{
    public VideoPageView() { InitializeComponent(); }
    internal VideoResolutionSelectorControl ResolutionSelector => resolutionSelector;
    internal VideoBitrateSelectorControl BitrateSelector => bitrateSelector;
    internal VideoFrameRateSelectorControl FrameRateSelector => frameRateSelector;
    internal ModernButton DiscardButton => discardButton;
    internal ModernButton ApplyButton => applyButton;
    internal Label QualityStatus => qualityStatus;
    internal Label Status => status;
}
