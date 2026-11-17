namespace VRPhoneScreenOverlay.SteamVR;

internal sealed partial class OpenVrPhoneMenuState
{
    private OpenVrMenuTarget _panelGestureTarget;
    private float _panelStartY;
    private int _panelStartScroll;
    private bool _panelDragging;
    private TranslationPanelHit? _panelTranslation;
    private ClipboardPanelHit? _panelClipboard;
    public bool PhoneConnected => Translation?.IsPhoneConnected ?? Clipboard?.IsPhoneConnected ?? !DisconnectedPlaceholder;

    public void SynchronizePhoneConnection()
    {
        if (PhoneConnected) { return; }
        ClipboardExpanded = false;
        _clipboardArmed = null;
        Clipboard?.SetPressed(null);
        if (_panelGestureTarget == OpenVrMenuTarget.ClipboardAction) { _panelGestureTarget = OpenVrMenuTarget.None; }
        _ = Clipboard?.Take();
        _ = Clipboard?.TakeScroll();
    }

    private bool ProcessPanelGesture(bool inside, OpenVrMenuTarget target, bool clicked,
        bool released, float? y, TranslationPanelHit? translation, ClipboardPanelHit? clipboard)
    {
        bool panel = inside && (target == OpenVrMenuTarget.TranslationAction && TranslationVisible ||
            target == OpenVrMenuTarget.ClipboardAction && ClipboardVisible && PhoneConnected);
        if (clicked && panel && y.HasValue)
        {
            _panelGestureTarget = target;
            _panelStartY = y.Value;
            _panelStartScroll = target == OpenVrMenuTarget.TranslationAction ? Translation?.Frame?.Scroll ?? 0 : Clipboard?.Frame?.Scroll ?? 0;
            _panelTranslation = translation; _panelClipboard = clipboard; _panelDragging = false;
            Translation?.SetPressed(translation); Clipboard?.SetPressed(clipboard);
            return true;
        }
        if (_panelGestureTarget == OpenVrMenuTarget.None) { return false; }
        if (!panel || target != _panelGestureTarget || !y.HasValue)
        {
            _panelGestureTarget = OpenVrMenuTarget.None;
            Translation?.SetPressed(null); Clipboard?.SetPressed(null);
            return true;
        }
        float delta = _panelStartY - y.Value;
        if (Math.Abs(delta) >= 12) { _panelDragging = true; }
        if (_panelDragging)
        {
            Translation?.SetPressed(null); Clipboard?.SetPressed(null);
            if (_panelStartY >= (target == OpenVrMenuTarget.TranslationAction ? 240 : 196) && _panelStartY < 944)
            {
                if (target == OpenVrMenuTarget.TranslationAction && Translation is { } translationChannel)
                { translationChannel.RequestScroll(Math.Clamp(_panelStartScroll + (int)delta, 0, translationChannel.Frame?.MaximumScroll ?? 0)); }
                else if (Clipboard is { } clipboardChannel)
                { clipboardChannel.RequestScroll(Math.Clamp(_panelStartScroll + (int)delta, 0, clipboardChannel.Frame?.MaximumScroll ?? 0)); }
            }
        }
        if (released)
        {
            if (!_panelDragging)
            {
                if (target == OpenVrMenuTarget.TranslationAction && translation is not null && translation == _panelTranslation)
                {
                    if (translation.Action == TranslationPanelAction.Close) { TranslationExpanded = false; }
                    else { Translation?.Submit(translation); }
                }
                if (target == OpenVrMenuTarget.ClipboardAction && clipboard is not null && clipboard == _panelClipboard)
                {
                    if (clipboard.Action == ClipboardPanelAction.Close) { ClipboardExpanded = false; }
                    else { Clipboard?.Submit(clipboard); }
                }
            }
            _panelGestureTarget = OpenVrMenuTarget.None;
            Translation?.SetPressed(null); Clipboard?.SetPressed(null);
        }
        return true;
    }
}
