using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32;

namespace VRPhoneScreenOverlay.Diagnostics;

internal static class IncidentEnvironmentCapture
{
    private static readonly string[] _runtimeNames = ["vrserver", "vrcompositor", "vrmonitor"];
    internal static IncidentEnvironment Capture()
    {
        List<IncidentDisplayDriver> drivers = [];
        string status = "not-found";
        bool truncated = false;
        try
        {
            using RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            // Microsoft display-driver software class. These are installed candidates,
            // not proof that a particular adapter/driver was selected by the renderer.
            using RegistryKey? display = machine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (display is not null)
            {
                string[] keys = display.GetSubKeyNames().Where(name => name.Length == 4 && name.All(char.IsAsciiDigit)).Take(17).ToArray();
                truncated = keys.Length > 16;
                foreach (string name in keys.Take(16))
                {
                    using RegistryKey? key = display.OpenSubKey(name);
                    if (key is null) { continue; }
                    string? version = key.GetValue("DriverVersion") as string;
                    if (version is not null && (version.Length > 64 || !version.All(c => char.IsAsciiDigit(c) || c == '.'))) { version = null; }
                    drivers.Add(new(SafeText(key.GetValue("DriverDesc") as string), version,
                        SafeText(key.GetValue("ProviderName") as string)));
                }
                status = "installed-registry-candidates";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { status = "collection-failed"; }
        return new(RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(), RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount, status, truncated, drivers.ToArray())
        { SteamVrProcesses = CaptureRuntimeProcesses() };
    }

    private static IncidentRuntimeProcess[] CaptureRuntimeProcesses()
    {
        List<IncidentRuntimeProcess> result = [];
        foreach (string name in _runtimeNames)
        {
            Process[] processes = [];
            try
            {
                processes = Process.GetProcessesByName(name);
                Process? process = processes.FirstOrDefault();
                string? version = process?.MainModule?.FileVersionInfo.FileVersion;
                if (version is not null && (version.Length > 80 || !version.All(c => char.IsAsciiDigit(c) || c is '.' or ',' or ' ')))
                { version = null; }
                result.Add(new(name, process is null ? "not-running-at-capture" : version is null ? "version-unavailable" : "sampled", version));
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
            { result.Add(new(name, "query-unavailable", null)); }
            finally { foreach (Process process in processes) { process.Dispose(); } }
        }
        return result.ToArray();
    }

    private static string? SafeText(string? value)
    {
        if (value is null) { return null; }
        string safe = DiagnosticSanitizer.SanitizeText(value);
        return safe.Length <= 128 ? safe : safe[..128];
    }
}
