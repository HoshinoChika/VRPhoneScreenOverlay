namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    internal void ConfigureClipboard(VRPhoneScreenOverlay.Session.ClipboardConfigurationStore configuration) =>
        _settingsView.ConfigureClipboard(configuration);
    internal Task CopyPhoneClipboardAsync(string text, CancellationToken token) => CopyPhoneClipboardGuardedAsync(text, () => true, token);
    internal async Task CopyPhoneClipboardGuardedAsync(string text, Func<bool> current, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await InvokeAsync(() => { if (current()) { Clipboard.SetText(text); } }, token).ConfigureAwait(false);
                return;
            }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < 2)
            { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }
}
