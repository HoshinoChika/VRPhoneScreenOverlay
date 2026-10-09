namespace VRPhoneScreenOverlay.App;

internal static class PhoneOverlayButtonPolicy
{
    internal static void Apply(ModernButton button, bool canStop, bool startReady, bool busy,
        bool wirelessBusy = false)
    {
        button.Tone = canStop ? UiButtonTone.Danger : startReady ? UiButtonTone.Success : UiButtonTone.Neutral;
        // Pairing must not prevent stopping an existing local session, but a new
        // session must wait for a pending connect/disconnect transaction to finish.
        button.Enabled = !busy && (canStop || (startReady && !wirelessBusy));
    }
}
