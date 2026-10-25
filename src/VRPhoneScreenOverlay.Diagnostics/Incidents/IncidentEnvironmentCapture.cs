using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VRPhoneScreenOverlay.Diagnostics;

internal static class IncidentEnvironmentCapture
{
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
            Environment.ProcessorCount, status, truncated, drivers.ToArray());
    }

    private static string? SafeText(string? value)
    {
        if (value is null) { return null; }
        string safe = DiagnosticSanitizer.SanitizeText(value);
        return safe.Length <= 128 ? safe : safe[..128];
    }
}
