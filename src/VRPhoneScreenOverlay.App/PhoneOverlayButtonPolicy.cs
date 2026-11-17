namespace VRPhoneScreenOverlay.App;

internal static class PhoneOverlayButtonPolicy
{
    internal static void ApplyVrMenu(ModernButton button, bool opened, bool runtimeReady, bool busy)
    {
        button.Text = opened ? "关闭 VR 浮窗" : "打开 VR 浮窗";
        button.Icon = opened ? UiIcons.Pause : UiIcons.Play;
        Apply(button, opened, runtimeReady, busy);
    }

    internal static void Apply(ModernButton button, bool canStop, bool startReady, bool busy,
        bool wirelessBusy = false)
    {
        button.Tone = canStop ? UiButtonTone.Danger : startReady ? UiButtonTone.Success : UiButtonTone.Neutral;
        // Pairing must not prevent stopping an existing local session, but a new
        // session must wait for a pending connect/disconnect transaction to finish.
        button.Enabled = !busy && (canStop || (startReady && !wirelessBusy));
    }
}
