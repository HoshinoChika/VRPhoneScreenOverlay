namespace VRPhoneScreenOverlay.SteamVR;

internal sealed partial class OpenVrPhoneMenuState
{
    private OpenVrMenuTarget _stickTarget;
    private long _stickTime;
    private double _stickPosition;

    // Uses the existing phone-operation hand axis (right by default), never a second input binding.
    public bool ProcessPanelStick(bool enabled, OpenVrMenuTarget target, float axis, long now)
    {
        bool translation = target == OpenVrMenuTarget.TranslationAction && TranslationVisible;
        bool clipboard = target == OpenVrMenuTarget.ClipboardAction && ClipboardVisible && PhoneConnected;
        if (!enabled || !ControlsVisible || (!translation && !clipboard) || !float.IsFinite(axis) || Math.Abs(axis) <= 0.2f)
        {
            _stickTarget = OpenVrMenuTarget.None;
            return false;
        }
        int maximum = translation ? Translation?.Frame?.MaximumScroll ?? 0 : Clipboard?.Frame?.MaximumScroll ?? 0;
        if (_stickTarget != target)
        {
            _stickTarget = target;
            _stickPosition = translation ? Translation?.Frame?.Scroll ?? 0 : Clipboard?.Frame?.Scroll ?? 0;
            _stickTime = now;
        }
        double seconds = Math.Clamp(now - _stickTime, 0, 50) / 1000d;
        _stickTime = now;
        // Up reveals earlier content; down reveals later content. Preserve subpixel movement between polls.
        double speed = Math.CopySign((Math.Min(Math.Abs(axis), 1) - 0.2) / 0.8, axis) * 900;
        int previous = (int)_stickPosition;
        _stickPosition = Math.Clamp(_stickPosition - speed * seconds, 0, maximum);
        if ((int)_stickPosition != previous)
        {
            if (translation) { Translation?.RequestScroll((int)_stickPosition); }
            else { Clipboard?.RequestScroll((int)_stickPosition); }
        }
        return true;
    }
}
