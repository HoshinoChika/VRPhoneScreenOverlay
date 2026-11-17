namespace VRPhoneScreenOverlay.SteamVR;

public static class OpenVrBindingRecovery
{
    private static readonly object _gate = new();

    public static OpenVrBindingPreparationSnapshot DiagnosticSnapshot =>
        OpenVrBindingStartup.GetDiagnosticSnapshot();

    public static OpenVrBindingResult PrepareChangedLocalBinding()
    {
        if (!OpenVrBindingStartup.HasSavedBindingChanged())
        {
            return new OpenVrBindingResult(
                true,
                OpenVrReasonCodes.LocalBindingUnchanged,
                "本地手柄绑定没有变化");
        }

        return PrepareLocalBinding();
    }

    public static bool HasSavedBindingChanged() =>
        OpenVrBindingStartup.HasSavedBindingChanged();

    public static OpenVrBindingResult PrepareLocalFiles() => OpenVrBindingStartup.Refresh();

    public static ValueTask<OpenVrBindingResult> ActivateLocalBindingAsync(CancellationToken token) => OpenVrBindingStartup.ActivateAsync(token);

    public static OpenVrBindingResult PrepareLocalBinding()
    {
        lock (_gate)
        {
            try
            {
                OpenVrBindingResult prepared = PrepareLocalFiles();
                return prepared.Succeeded ? OpenVrBindingStartup.ActivateAsync(CancellationToken.None, OpenVrRuntimeHost.Shutdown).AsTask().GetAwaiter().GetResult() : prepared;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or
                    InvalidOperationException)
            {
                return new OpenVrBindingResult(
                    false,
                    OpenVrReasonCodes.BindingDefaultRestoreFailed,
                    "默认绑定恢复失败，请重启 SteamVR 后重试");
            }
        }
    }
}
