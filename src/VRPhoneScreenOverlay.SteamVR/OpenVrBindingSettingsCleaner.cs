using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

public static class OpenVrBindingSettingsCleaner
{
    public static OpenVrBindingResult ActivateLocalBinding() => Prepare(null, clearSelection: true);

    public static OpenVrBindingResult EnrichLocalBinding(OpenVrControllerHand preparedHand) => Prepare(preparedHand, clearSelection: false);

    private static OpenVrBindingResult Prepare(OpenVrControllerHand? preparedHand, bool clearSelection)
    {
        bool initialized = false;
        try
        {
            EVRInitError initializationError = EVRInitError.None;
            _ = OpenVR.Init(ref initializationError, EVRApplicationType.VRApplication_Utility);
            if (initializationError != EVRInitError.None)
            {
                return new OpenVrBindingResult(
                    false,
                    OpenVrReasonCodes.InitializationFailed,
                    "无法连接 SteamVR，尚未切换到程序本地绑定");
            }

            initialized = true;
            using (CancellationTokenSource profileRead = new(TimeSpan.FromSeconds(2)))
            {
                OpenVrInputCaptureProfiles.EnrichAsync(profileRead.Token, preparedHand).GetAwaiter().GetResult();
            }
            if (!clearSelection)
            {
                return new OpenVrBindingResult(true, OpenVrReasonCodes.LocalBindingDriverEnriched, "已补齐本地绑定的驱动输入配置");
            }
            EVRSettingsError settingsError = EVRSettingsError.None;
            OpenVR.Settings.RemoveSection(OpenVrInputManifest.ApplicationKey, ref settingsError);
            return settingsError == EVRSettingsError.None
                ? new OpenVrBindingResult(
                    true,
                    OpenVrReasonCodes.LocalBindingActivated,
                    "已清除 SteamVR 云端或 Workshop 绑定选择并启用程序本地绑定")
                : new OpenVrBindingResult(
                    false,
                    OpenVrReasonCodes.LocalBindingActivationFailed,
                    "无法清除 SteamVR 托管绑定选择，尚未切换到程序本地绑定");
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new OpenVrBindingResult(
                false,
                OpenVrReasonCodes.LocalBindingActivationFailed,
                "SteamVR 本地绑定准备工具无法启动 OpenVR");
        }
        finally
        {
            if (initialized)
            {
                OpenVR.Shutdown();
            }
        }
    }
}
