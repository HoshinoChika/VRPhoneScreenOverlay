using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRPhoneScreenOverlay.SteamVR;

internal static partial class OpenVrRuntimeAvailability
{
    internal static bool CanInitializeUtility(bool running, bool namespaceAvailable) => !running || namespaceAvailable;
    internal static bool CanInitializeUtility() => CanInitializeUtility(HasAnyRuntimeProcess(), IsNamespaceAvailable());
    internal static OpenVrBindingResult NamespaceUnavailable() => new(false, "OPENVR_NAMESPACE_UNAVAILABLE",
        "SteamVR 正在运行，但通信入口不可用；已暂停连接尝试，请在方便时重启 SteamVR。")
    { StartupDiagnostic = new("namespace-preflight") };
    public static bool IsNamespaceAvailable() => WaitNamedPipe(@"\\.\pipe\SteamVR_Namespace", 100);

    [LibraryImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WaitNamedPipe(string name, uint timeout);

    private static bool HasAnyRuntimeProcess() => HasAnyRuntimeProcess(HasProcess("vrserver"), HasProcess("vrcompositor"), HasProcess("vrmonitor"));
    internal static bool HasAnyRuntimeProcess(bool server, bool compositor, bool monitor) => server || compositor || monitor;

    public static bool IsRunning()
        => IsInteractiveRuntimeRunning(HasProcess("vrserver"), HasProcess("vrcompositor"), HasProcess("vrmonitor"));

    internal static bool IsInteractiveRuntimeRunning(bool server, bool compositor, bool monitor) =>
        server && (compositor || monitor);

    private static bool HasProcess(string name)
    {
        Process[] processes = Process.GetProcessesByName(name);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes) { process.Dispose(); }
        }
    }
}
