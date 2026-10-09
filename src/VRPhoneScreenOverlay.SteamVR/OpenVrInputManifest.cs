using System.Text;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrInputManifest
{
    public const string ApplicationKey = "local.spacedraglite.desktop.v1";
    public const string ActionSetPath = "/actions/main";
    public const string PointerActionSetPath = "/actions/pointer";
    public const string PhoneButtonStateActionSetPath = "/actions/phonebuttonstate";

    public static (string ApplicationManifest, string ActionManifest) ResolveFiles()
    {
        string applicationManifest = ResolveApplicationManifest();
        string actionManifest = OpenVrBindingStartup.EnsurePrepared();
        if (!File.Exists(applicationManifest) || !File.Exists(actionManifest))
        {
            throw new FileNotFoundException("SteamVR 应用或动作清单未随程序发布");
        }

        return (applicationManifest, actionManifest);
    }

    internal static string ResolveApplicationManifest()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "manifest.vrmanifest");
        return File.Exists(path) ? path : throw new FileNotFoundException("SteamVR 应用清单未随程序发布");
    }

    public static EVRInputError RegisterAndSubmit()
    {
        (string applicationManifest, string actionManifest) = ResolveFiles();
        RegisterApplicationManifest(applicationManifest);
        return OpenVR.Input.SetActionManifestPath(actionManifest);
    }

    public static ulong GetActionSet() => GetActionSet(ActionSetPath, "动作集");

    public static ulong GetPointerActionSet() => GetActionSet(
        PointerActionSetPath,
        "手机射线动作集");

    public static ulong GetPhoneButtonStateActionSet() => GetActionSet(
        PhoneButtonStateActionSetPath,
        "手机输入原始状态动作集");

    private static ulong GetActionSet(string path, string operation)
    {
        ulong actionSet = OpenVR.k_ulInvalidActionSetHandle;
        Check(OpenVR.Input.GetActionSetHandle(path, ref actionSet), operation);
        return actionSet;
    }

    public static OpenVrBindingResult OpenBindingUi(ulong actionSet)
    {
        EVRInputError result = OpenVR.Input.OpenBindingUI(
            ApplicationKey,
            actionSet,
            OpenVR.k_ulInvalidInputValueHandle,
            true);
        return result == EVRInputError.None
            ? new OpenVrBindingResult(
                true,
                OpenVrReasonCodes.BindingUiOpened,
                "SteamVR 绑定页面已打开；拖拽、触控、返回、桌面、" +
                "最近任务、控制栏和截屏现在位于同一动作集")
            : new OpenVrBindingResult(
                false,
                OpenVrReasonCodes.BindingUiFailed,
                $"SteamVR 手柄绑定页面打开失败：{result}");
    }

    internal static void RegisterApplicationManifest(string applicationManifest, bool? autoLaunch = null)
    {
        EVRApplicationError propertyError = EVRApplicationError.None;
        StringBuilder binaryPath = new(2048);
        OpenVR.Applications.GetApplicationPropertyString(
            ApplicationKey,
            EVRApplicationProperty.BinaryPath_String,
            binaryPath,
            (uint)binaryPath.Capacity,
            ref propertyError);
        if (propertyError == EVRApplicationError.None && binaryPath.Length > 0)
        {
            string registeredBinary = binaryPath.ToString();
            if (!Path.IsPathRooted(registeredBinary))
            {
                EVRApplicationError directoryError = EVRApplicationError.None;
                StringBuilder workingDirectory = new(2048);
                OpenVR.Applications.GetApplicationPropertyString(
                    ApplicationKey,
                    EVRApplicationProperty.WorkingDirectory_String,
                    workingDirectory,
                    (uint)workingDirectory.Capacity,
                    ref directoryError);
                if (directoryError == EVRApplicationError.None && workingDirectory.Length > 0)
                {
                    registeredBinary = Path.Combine(workingDirectory.ToString(), registeredBinary);
                }
            }

            string? registeredDirectory = Path.GetDirectoryName(registeredBinary);
            if (!string.IsNullOrWhiteSpace(registeredDirectory))
            {
                // Both shipped layouts launch the root EXE. A portable manifest
                // lives under app/, while the earlier flat layout kept it beside the EXE.
                foreach (string relativeManifest in new[] { "manifest.vrmanifest", "app/manifest.vrmanifest" })
                {
                    string registeredManifest = Path.GetFullPath(
                        Path.Combine(registeredDirectory, relativeManifest));
                    if (!string.Equals(
                        registeredManifest,
                        applicationManifest,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        _ = OpenVR.Applications.RemoveApplicationManifest(registeredManifest);
                    }
                }
            }
        }

        EVRApplicationError addError = OpenVR.Applications.AddApplicationManifest(
            applicationManifest,
            false);
        if (addError != EVRApplicationError.None)
        {
            throw new OpenVrApplicationRegistrationException("清单注册", addError);
        }

        // Ordinary input registration must preserve SteamVR's saved preference.
        // Only the explicit startup-setting path changes autolaunch.
        if (autoLaunch is not bool enabled) { return; }
        EVRApplicationError launchError = OpenVR.Applications.SetApplicationAutoLaunch(
            ApplicationKey, enabled);
        if (launchError != EVRApplicationError.None)
        {
            throw new OpenVrApplicationRegistrationException("自动启动设置", launchError);
        }
        if (OpenVR.Applications.GetApplicationAutoLaunch(ApplicationKey) != enabled)
        {
            throw new OpenVrApplicationRegistrationException("自动启动状态确认", EVRApplicationError.None);
        }
    }

    private static void Check(EVRInputError error, string operation)
    {
        if (error != EVRInputError.None)
        {
            throw new InvalidOperationException($"{operation}失败：{error}");
        }
    }
}

public static class OpenVrBindingUiLauncher
{
    private static readonly object _gate = new();

    public static OpenVrBindingResult Open() => Prepare(openUi: true);

    public static OpenVrBindingResult Prepare() => Prepare(openUi: false);

    private static OpenVrBindingResult Prepare(bool openUi)
    {
        lock (_gate)
        {
            try
            {
                EVRInitError initializationError = EVRInitError.None;
                _ = OpenVrRuntimeHost.GetOrStart(ref initializationError);
                if (initializationError != EVRInitError.None)
                {
                    return new OpenVrBindingResult(
                        false,
                        OpenVrReasonCodes.InitializationFailed,
                        $"SteamVR 连接失败：{initializationError}。请确认 SteamVR 正在运行");
                }

                EVRInputError manifestError = OpenVrRuntimeHost.EnsureActionManifestSubmitted();
                if (manifestError is not EVRInputError.None and not EVRInputError.IPCError)
                {
                    return new OpenVrBindingResult(
                        false,
                        OpenVrReasonCodes.BindingUiFailed,
                        $"SteamVR 动作清单加载失败：{manifestError}");
                }

                if (!openUi)
                {
                    return new(true, OpenVrReasonCodes.InputReady, "SteamVR 手柄绑定已准备就绪");
                }

                ulong actionSet = OpenVrInputManifest.GetActionSet();
                return OpenVrInputManifest.OpenBindingUi(actionSet);
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or InvalidOperationException or
                    DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return new OpenVrBindingResult(
                    false,
                    OpenVrReasonCodes.BindingUiFailed,
                    "SteamVR 手柄绑定页面打开失败，请确认 SteamVR 正在运行");
            }
        }
    }

    public static void Shutdown() => OpenVrRuntimeHost.Shutdown();
}
