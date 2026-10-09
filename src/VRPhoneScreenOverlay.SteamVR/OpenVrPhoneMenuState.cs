using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

internal enum OpenVrMenuTarget
{
    None, Dot, PhoneToggle, Opacity, PlayspaceToggle, MultiplierDown, MultiplierUp,
    ScreenGuardToggle, KeypadToggle, Digit0, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Backspace, Confirm,
    MotionSettings, MotionBack, FlingToggle, StrengthDown, StrengthUp, GravityDown, GravityUp, FrictionDown, FrictionUp, ResetMode, MotionSave,
    ResetModePrevious,
    VideoSettings, VideoBack, ResolutionDown, ResolutionUp, BitrateDown, BitrateUp, FrameRateDown, FrameRateUp, VideoApply,
}

internal readonly record struct OpenVrMenuChange(
    bool Consumed = false,
    bool VisibilityChanged = false,
    bool OpacityChanged = false,
    OpenVrPlayspaceControlRequest? Playspace = null,
    OpenVrPhoneInputCommand? PhoneCommand = null);

// User intent is separate from temporary dashboard/tracking suppression. This
// state contains no pose: hide/show cannot move or resize the phone by accident.
internal sealed class OpenVrPhoneMenuState
{
    public OpenVrVideoSettingsChannel? VideoSettings { get; set; }
    public bool VideoExpanded { get; private set; }
    public OpenVrVideoOptions VideoDraft { get; private set; } = new(100, 16, 60);
    private OpenVrVideoOptions _videoBaseline = new(100, 16, 60);
    private OpenVrPlayspaceMotionOptions _motionBaseline = OpenVrPlayspaceMotionOptions.Default;

    public void SynchronizeSettings(OpenVrSharedPlayspaceInput playspace)
    {
        if (VideoExpanded && VideoSettings is not null && VideoSettings.Current is { } current && current != _videoBaseline)
        {
            VideoDraft = current with
            {
                Resolution = current.Resolution != _videoBaseline.Resolution ? current.Resolution : VideoDraft.Resolution,
                Bitrate = current.Bitrate != _videoBaseline.Bitrate ? current.Bitrate : VideoDraft.Bitrate,
                FrameRate = current.FrameRate != _videoBaseline.FrameRate ? current.FrameRate : VideoDraft.FrameRate,
            };
            _videoBaseline = current;
        }
        if (MotionExpanded && playspace.MotionOptions is { } motion && motion != _motionBaseline)
        {
            MotionDraft = motion;
            _motionBaseline = motion;
        }
    }
    private bool _previousPressed = true;
    private bool _sliderCaptured;
    public bool OpacityCaptured => _sliderCaptured;
    private OpenVrMenuTarget _repeatTarget;
    private long _repeatStarted;
    private long _nextRepeat;
    private bool _ready;
    public bool MotionExpanded { get; private set; }
    public OpenVrPlayspaceMotionOptions MotionDraft { get; private set; } = OpenVrPlayspaceMotionOptions.Default;
    private bool _previousRecall = true;
    private bool? _locked;
    private bool _keypadExpanded;
    public bool ScreenGuardEnabled { get; private set; }
    public bool ScreenGuardFaulted { get; private set; }
    public void SetScreenGuard(bool enabled, bool faulted)
    {
        ScreenGuardEnabled = enabled;
        ScreenGuardFaulted = faulted;
    }
    public bool PhoneHidden { get; private set; }
    public bool Expanded { get; private set; }
    public bool DashboardVisible { get; private set; }
    public bool KeypadEnabled { get; private set; }
    public int OpacityPercent { get; private set; } = 100;
    public int EnteredDigitCount { get; private set; }
    public int InputRevision { get; private set; }
    public bool SuppressPhoneTouch { get; private set; } = true;
    public bool ControlsVisible => _ready && !DashboardVisible && !PhoneHidden;
    public bool PhoneVisible => _ready && !DashboardVisible && !PhoneHidden;
    public bool SidebarVisible => ControlsVisible && Expanded;
    public bool KeypadVisible => PhoneVisible && KeypadEnabled && _keypadExpanded;
    public bool IsSurfaceVisible(OpenVrPhoneSurface surface) => surface switch
    {
        OpenVrPhoneSurface.Dot => ControlsVisible,
        OpenVrPhoneSurface.Menu => SidebarVisible,
        OpenVrPhoneSurface.Keypad => KeypadVisible,
        _ => PhoneVisible,
    };

    public void SetLocked(bool? locked)
    {
        if (!KeypadEnabled) { _locked = null; return; }
        if (locked is null || locked == _locked) { return; }
        _locked = locked;
        _keypadExpanded = locked.Value;
        EnteredDigitCount = 0;
    }

    public bool ProcessRecall(bool pressed)
    {
        if (!_ready || DashboardVisible) { _previousRecall = true; return false; }
        bool clicked = pressed && !_previousRecall;
        _previousRecall = pressed;
        if (!clicked || !PhoneHidden) { return false; }
        PhoneHidden = false;
        Expanded = false;
        MotionExpanded = false;
        VideoExpanded = false;
        ResetInput();
        InputRevision++;
        return true;
    }

    public void SetEnvironment(bool ready, bool dashboard, bool keypadEnabled)
    {
        if (DashboardVisible != dashboard || _ready != ready) { ResetInput(); InputRevision++; }
        _ready = ready;
        DashboardVisible = dashboard;
        KeypadEnabled = keypadEnabled;
        if (!keypadEnabled) { EnteredDigitCount = 0; _keypadExpanded = false; _locked = null; }
    }

    private static int Step(IReadOnlyList<int> values, int current, bool up)
    {
        if (up) { foreach (int value in values) { if (value > current) { return value; } } }
        else { for (int i = values.Count - 1; i >= 0; i--) { if (values[i] < current) { return values[i]; } } }
        return current;
    }

    public void ResetInput()
    {
        _previousPressed = true;
        _sliderCaptured = false;
        _repeatTarget = OpenVrMenuTarget.None;
        SuppressPhoneTouch = true;
    }

    public OpenVrMenuChange ProcessInput(bool validRay, bool insideMenu, OpenVrMenuTarget target,
        float sliderFraction, bool pressed, OpenVrSharedPlayspaceInput playspace, long? timestampMilliseconds = null)
    {
        if (!ControlsVisible || !validRay)
        {
            ResetInput();
            return default;
        }
        bool clicked = pressed && !_previousPressed;
        _previousPressed = pressed;
        long now = timestampMilliseconds ?? Environment.TickCount64;
        int repeatUnits = 1;
        bool repeatable = target is OpenVrMenuTarget.MultiplierDown or OpenVrMenuTarget.MultiplierUp or
            OpenVrMenuTarget.StrengthDown or OpenVrMenuTarget.StrengthUp or OpenVrMenuTarget.GravityDown or
            OpenVrMenuTarget.GravityUp or OpenVrMenuTarget.FrictionDown or OpenVrMenuTarget.FrictionUp or
            OpenVrMenuTarget.ResolutionDown or OpenVrMenuTarget.ResolutionUp or OpenVrMenuTarget.BitrateDown or
            OpenVrMenuTarget.BitrateUp or OpenVrMenuTarget.FrameRateDown or OpenVrMenuTarget.FrameRateUp;
        if (!pressed || !insideMenu || !repeatable) { _repeatTarget = OpenVrMenuTarget.None; }
        else if (clicked) { _repeatTarget = target; _repeatStarted = now; _nextRepeat = now + 400; }
        else if (target == _repeatTarget && now >= _nextRepeat)
        {
            clicked = true;
            _nextRepeat = now + 60;
            repeatUnits = now - _repeatStarted >= 2000 ? 5 : 1;
        }
        else if (target != _repeatTarget) { _repeatTarget = OpenVrMenuTarget.None; }
        if (!pressed)
        {
            _sliderCaptured = false;
            SuppressPhoneTouch = false;
            return default;
        }
        if (!insideMenu)
        {
            if (clicked && Expanded)
            {
                Expanded = false;
                DismissHiddenEntry();
                SuppressPhoneTouch = true;
                return new(Consumed: true);
            }
            return default;
        }
        if (target == OpenVrMenuTarget.Opacity && Expanded && (clicked || _sliderCaptured))
        {
            _sliderCaptured = true;
            SuppressPhoneTouch = true;
            int opacity = float.IsFinite(sliderFraction) ? Math.Clamp((int)MathF.Round(sliderFraction * 100), 0, 100) : OpacityPercent;
            bool changed = opacity != OpacityPercent;
            OpacityPercent = opacity;
            return new(Consumed: true, OpacityChanged: changed);
        }
        if (!clicked) { return default; }
        SuppressPhoneTouch = true;
        if (target == OpenVrMenuTarget.Dot)
        {
            Expanded = !Expanded;
            if (!Expanded) { DismissHiddenEntry(); }
            return new(Consumed: true);
        }
        if (target == OpenVrMenuTarget.KeypadToggle && (Expanded || KeypadVisible) && KeypadEnabled && !PhoneHidden)
        {
            _keypadExpanded = !_keypadExpanded;
            EnteredDigitCount = 0;
            return new(Consumed: true, PhoneCommand: _keypadExpanded ? new(PhoneInputCommandKind.WakeScreen) : null);
        }
        bool keypadTarget = target is >= OpenVrMenuTarget.Digit0 and <= OpenVrMenuTarget.Confirm;
        if (!Expanded && !(KeypadVisible && keypadTarget)) { return default; }
        if (target == OpenVrMenuTarget.VideoSettings && VideoSettings is not null)
        {
            VideoExpanded = true;
            MotionExpanded = false;
            VideoDraft = _videoBaseline = VideoSettings.Current;
            VideoSettings.ClearResult();
            return new(Consumed: true);
        }
        if (VideoExpanded && !keypadTarget)
        {
            if (target == OpenVrMenuTarget.VideoBack) { VideoExpanded = false; return new(Consumed: true); }
            if (VideoSettings is null || VideoSettings.State == OpenVrVideoApplyState.Applying) { return new(Consumed: true); }
            OpenVrVideoOptions previousVideo = VideoDraft;
            VideoDraft = target switch
            {
                OpenVrMenuTarget.ResolutionDown or OpenVrMenuTarget.ResolutionUp => VideoDraft with
                { Resolution = Step(VideoSettings.Resolutions, VideoDraft.Resolution, target == OpenVrMenuTarget.ResolutionUp) },
                OpenVrMenuTarget.BitrateDown or OpenVrMenuTarget.BitrateUp => VideoDraft with
                { Bitrate = Step(VideoSettings.Bitrates, VideoDraft.Bitrate, target == OpenVrMenuTarget.BitrateUp) },
                OpenVrMenuTarget.FrameRateDown or OpenVrMenuTarget.FrameRateUp => VideoDraft with
                { FrameRate = Step(VideoSettings.FrameRates, VideoDraft.FrameRate, target == OpenVrMenuTarget.FrameRateUp) },
                _ => VideoDraft,
            };
            if (VideoDraft != previousVideo) { VideoSettings.ClearResult(); }
            if (target == OpenVrMenuTarget.VideoApply) { VideoSettings.Request(VideoDraft); }
            return new(Consumed: true);
        }
        if (target == OpenVrMenuTarget.MotionSettings)
        {
            MotionExpanded = true;
            MotionDraft = _motionBaseline = playspace.MotionOptions ?? OpenVrPlayspaceMotionOptions.Default;
            return new(Consumed: true);
        }
        if (target == OpenVrMenuTarget.FlingToggle)
        {
            return new(Consumed: true, Playspace: new(playspace.PlayspaceEnabled, playspace.Multiplier,
                !playspace.FlingEnabled));
        }
        if (target is OpenVrMenuTarget.MultiplierDown or OpenVrMenuTarget.MultiplierUp)
        {
            return new(Consumed: true, Playspace: new(playspace.PlayspaceEnabled,
                NextMultiplier(playspace.Multiplier, target == OpenVrMenuTarget.MultiplierUp)));
        }
        if (MotionExpanded && !keypadTarget)
        {
            OpenVrPlayspaceMotionOptions previous = MotionDraft;
            switch (target)
            {
                case OpenVrMenuTarget.MotionBack: MotionExpanded = false; return new(Consumed: true);
                case OpenVrMenuTarget.StrengthDown:
                case OpenVrMenuTarget.StrengthUp:
                    MotionDraft = MotionDraft with
                    {
                        FlingStrength = Math.Clamp(MathF.Round(MotionDraft.FlingStrength +
                        repeatUnits * (target == OpenVrMenuTarget.StrengthUp ? 0.1f : -0.1f), 1), 0, 20)
                    }; break;
                case OpenVrMenuTarget.GravityDown:
                case OpenVrMenuTarget.GravityUp:
                    MotionDraft = MotionDraft with
                    {
                        Gravity = Math.Clamp(MathF.Round(MotionDraft.Gravity +
                        repeatUnits * (target == OpenVrMenuTarget.GravityUp ? 0.1f : -0.1f), 1), 0, 30)
                    }; break;
                case OpenVrMenuTarget.FrictionDown:
                case OpenVrMenuTarget.FrictionUp:
                    MotionDraft = MotionDraft with
                    {
                        Friction = Math.Clamp(MotionDraft.Friction +
                        repeatUnits * (target == OpenVrMenuTarget.FrictionUp ? 1 : -1), 0, 999)
                    }; break;
                case OpenVrMenuTarget.ResetMode:
                case OpenVrMenuTarget.ResetModePrevious:
                    MotionDraft = MotionDraft with { ResetAllOffsets = !MotionDraft.ResetAllOffsets }; break;
            }
            return previous == MotionDraft ? new(Consumed: true)
                : new(Consumed: true, Playspace: new(playspace.PlayspaceEnabled, playspace.Multiplier,
                    playspace.FlingEnabled, MotionDraft));
        }
        switch (target)
        {
            case OpenVrMenuTarget.PhoneToggle:
                PhoneHidden = !PhoneHidden;
                // Hiding folds the group; restoring leaves the sidebar open.
                // The presentation owner places the phone in front of the head.
                if (PhoneHidden) { Expanded = false; MotionExpanded = false; VideoExpanded = false; _previousRecall = true; _keypadExpanded = false; EnteredDigitCount = 0; }
                InputRevision++;
                return new(Consumed: true, VisibilityChanged: true);
            case OpenVrMenuTarget.ScreenGuardToggle:
                return new(Consumed: true, PhoneCommand: new(PhoneInputCommandKind.ToggleScreenGuard));
            case OpenVrMenuTarget.PlayspaceToggle:
                return new(Consumed: true, Playspace: new(!playspace.PlayspaceEnabled, playspace.Multiplier));
        }
        if (KeypadVisible && target is >= OpenVrMenuTarget.Digit0 and <= OpenVrMenuTarget.Confirm)
        {
            if (target <= OpenVrMenuTarget.Digit9) { EnteredDigitCount = Math.Min(16, EnteredDigitCount + 1); }
            else if (target == OpenVrMenuTarget.Backspace) { EnteredDigitCount = Math.Max(0, EnteredDigitCount - 1); }
            else { EnteredDigitCount = 0; }
            OpenVrPhoneInputCommand command = target switch
            {
                OpenVrMenuTarget.Backspace => new(PhoneInputCommandKind.UnlockBackspace),
                OpenVrMenuTarget.Confirm => new(PhoneInputCommandKind.UnlockConfirm),
                _ => new(PhoneInputCommandKind.UnlockDigit, UnlockDigit: (int)target - (int)OpenVrMenuTarget.Digit0),
            };
            return new(Consumed: true, PhoneCommand: command);
        }
        return new(Consumed: true);
    }

    private void DismissHiddenEntry()
    {
        if (!PhoneHidden) { return; }
        MotionExpanded = false;
        _keypadExpanded = false;
        EnteredDigitCount = 0;
        InputRevision++;
    }

    internal static int NextMultiplier(float current, bool increase)
    {
        ReadOnlySpan<int> steps = [1, 5, 10, 20, 40];
        if (increase)
        {
            foreach (int step in steps) { if (step > current) { return step; } }
            return 40;
        }
        for (int index = steps.Length - 1; index >= 0; index--) { if (steps[index] < current) { return steps[index]; } }
        return 1;
    }
}
