using System.ComponentModel;
using System.Diagnostics;

namespace VRPhoneScreenOverlay.SteamVR;

public static class OpenVrMenuRuntime
{
    // Called once per lifecycle tick, never per frame. Start time distinguishes
    // a restarted server even when the unavailable interval was not observed.
    public static long ReadIdentity()
    {
        if (!OpenVrRuntimeAvailability.IsRunning()) { return 0; }
        Process[] servers = Process.GetProcessesByName("vrserver");
        try
        {
            foreach (Process server in servers)
            {
                try { return server.StartTime.ToUniversalTime().Ticks; }
                catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
            }
            return 0;
        }
        finally { foreach (Process server in servers) { server.Dispose(); } }
    }

    // All native consumers must be stopped before calling this method.
    public static void ResetAfterConsumersStopped() => OpenVrRuntimeHost.Shutdown();
}
