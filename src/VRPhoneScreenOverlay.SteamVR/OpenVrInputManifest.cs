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
        var registration = OpenVrApplicationRegistration.Apply(applicationManifest, null,
            new OpenVrApplicationRegistry(), runtimeOnly: true);
        if (registration.Stage != "confirmed") { throw new OpenVrApplicationRegistrationException(registration); }
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
                "已请求打开 SteamVR 手柄绑定页面")
            : new OpenVrBindingResult(
                false,
                OpenVrReasonCodes.BindingUiFailed,
                $"SteamVR 手柄绑定页面打开失败：{result}");
    }

    internal static VRPhoneScreenOverlay.Contracts.SteamVrStartupDiagnostic RegisterApplicationManifest(string applicationManifest, bool? autoLaunch = null)
    {
        var evidence = OpenVrApplicationRegistration.Apply(applicationManifest, autoLaunch, new OpenVrApplicationRegistry());
        if (evidence.Stage != "confirmed") { throw new OpenVrApplicationRegistrationException(evidence); }
        return evidence;
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
                    return new(true, OpenVrReasonCodes.InputReady, "SteamVR 手柄输入已就绪");
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
