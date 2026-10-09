using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App.Views;

public partial class ControllerBindingsView : UserControl
{
    private readonly List<ControllerBindingChoice> _choices = [.. OpenVrBindingGuide.Choices];
    private ControllerBindingGuide? _pendingGuide;
    private (OpenVrBindingHealthState State, bool Show, string Message)? _pendingNotice;
    internal bool IsRestoringDefaults { get; private set; }
    public ControllerBindingsView()
    {
        InitializeComponent();
        openBindingsButton.SetBundledApplicationIcon(BundledApplicationIcon.Steam);
    }
    internal BindingGuideSelectorControl ControllerSelector => controllerSelector;
    internal ModernButton OpenBindingsButton => openBindingsButton;
    internal ModernButton RestoreBindingsButton => restoreBindingsButton;
    internal ControllerHandSelectorControl HandSelector => handSelector;
    internal Label OperationStatus => status;
    internal ModernButton RefreshButton => refreshButton;
    internal SteamVrBindingNotice BindingNotice => bindingNotice;

    internal void BeginGuideRead()
    {
        status.Text = "正在读取绑定…";
        status.ForeColor = UiPalette.TextSecondary;
    }

    internal void FailGuideRead()
    {
        status.Text = "绑定读取失败，请刷新重试。";
        status.ForeColor = UiPalette.Danger;
    }

    internal void SetGuide(ControllerBindingGuide guide)
    {
        if (IsRestoringDefaults) { _pendingGuide = guide; return; }
        ApplyGuide(guide, updateStatus: true);
    }

    internal void BeginRestoreDefaults()
    {
        IsRestoringDefaults = true;
        _pendingGuide = null;
        _pendingNotice = null;
    }

    internal void EndRestoreDefaults()
    {
        IsRestoringDefaults = false;
        if (_pendingGuide is { } guide) { ApplyGuide(guide, updateStatus: false); }
        if (_pendingNotice is { } notice) { bindingNotice.UpdateNotice(notice.State, notice.Show, notice.Message); }
        _pendingGuide = null;
        _pendingNotice = null;
    }

    internal void UpdateBindingNotice(OpenVrBindingHealthState state, bool show, string message)
    {
        // One latest-value presentation slot. Input/media services keep running;
        // background notices cannot move the instruction area during a reset.
        if (IsRestoringDefaults) { _pendingNotice = (state, show, message); return; }
        bindingNotice.UpdateNotice(state, show, message);
    }

    private void ApplyGuide(ControllerBindingGuide guide, bool updateStatus)
    {
        if (guide.SelectionKey is { } selectionKey)
        {
            controllerSelector.SetNodes(_choices.Select(value => new FixedChoiceNode<string>(value.Key,
                value.Key == OpenVrBindingGuide.GenericKey && guide.Unrecognized ? "通用（无法识别）" : value.Label)), selectionKey);
        }
        else if (guide.ControllerType.Length > 0)
        {
            controllerSelector.TryGetSelectedValue(out string selectedKey);
            ControllerBindingChoice? choice = _choices.FirstOrDefault(value => value.Key == selectedKey && value.ControllerType == guide.ControllerType);
            if (choice is null)
            {
                ControllerBindingChoice[] products = OpenVrBindingGuide.Choices.Where(value => value.ControllerType == guide.ControllerType).ToArray();
                if (products.Length == 1) { choice = products[0]; }
                else
                {
                    // A shared driver identifier alone cannot identify a retail brand.
                    string currentKey = OpenVrBindingGuide.CurrentBindingKey(guide.ControllerType);
                    choice = _choices.FirstOrDefault(value => value.Key == currentKey);
                    if (choice is null)
                    {
                        choice = new(guide.ControllerType, "当前绑定", currentKey);
                        _choices.Add(choice);
                    }
                }
            }
            controllerSelector.SetNodes(_choices.Select(value => new FixedChoiceNode<string>(value.Key, value.Label)), choice.Key);
        }
        if (updateStatus && status.Text != guide.Source) { status.Text = guide.Source; }
        instructions.SetGuide(guide);
    }
}
