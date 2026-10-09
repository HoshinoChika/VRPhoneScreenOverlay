using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRPhoneScreenOverlay.SteamVR;

internal static partial class OpenVrRuntimeAvailability
{
    public static bool IsNamespaceAvailable() => WaitNamedPipe(@"\\.\pipe\SteamVR_Namespace", 100);

    [LibraryImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WaitNamedPipe(string name, uint timeout);

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
