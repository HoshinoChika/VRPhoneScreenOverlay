using Microsoft.Win32;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Maintenance;

internal static class UpdateRecoveryStartup
{
    private const string _runOnce = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    internal static string ValueName(string token) => "!VRPhoneScreenOverlay.UpdateRecovery." + token;

    internal static string Command(string executable, string token)
    {
        string full = Path.GetFullPath(executable);
        if (token.Length != 32 || !token.All(Uri.IsHexDigit) ||
            Path.GetFileName(full) != AppIdentity.MaintenanceExecutableName ||
            !string.Equals(Path.GetFileName(Path.GetDirectoryName(full)), token, StringComparison.OrdinalIgnoreCase) || full.Contains('"'))
        { throw new IOException("Invalid recovery executable."); }
        string command = $"\"{full}\" --recover-update";
        if (command.Length > 260) { throw new IOException("Recovery startup command exceeds the Windows limit."); }
        return command;
    }

    internal static UpdateRecoveryJournal Prepare(string runner, string install, string stage, string token, Action register)
    {
        UpdateRecoveryJournal? journal = null;
        try
        {
            journal = UpdateRecoveryJournal.Create(runner, install, stage, token);
            register();
            UpdateRecoveryJournal result = journal;
            journal = null;
            return result;
        }
        catch (Exception error)
        {
            try { journal?.FinishSetupFailure(error.HResult); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw;
        }
        finally { journal?.Dispose(); }
    }

    internal static void Register(string executable, string token)
    {
        if (!File.Exists(executable)) { throw new IOException("Recovery executable is missing."); }
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(_runOnce, writable: true);
        key.SetValue(ValueName(token), Command(executable, token), RegistryValueKind.String);
        key.Flush();
    }

    internal static void Remove(string token)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_runOnce, writable: true);
            key?.DeleteValue(ValueName(token), throwOnMissingValue: false);
            key?.Flush();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { /* A finished journal makes a leftover RunOnce entry harmless. */ }
    }
}
