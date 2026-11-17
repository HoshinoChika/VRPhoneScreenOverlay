using System.Diagnostics;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Maintenance;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--diagnostic-capture")
        {
            return Diagnostics.DiagnosticCaptureObserver.Run(args);
        }

        if (args.Length > 0 && args[0] == "--diagnostic-monitor")
        {
            return Diagnostics.IncidentExitMonitor.Run(args);
        }

        if (args.Length == 1 && args[0] == "--recover-update")
        {
            try
            {
                using UpdateRecoveryJournal recovery = UpdateRecoveryJournal.Open(RunnerDirectory());
                recovery.Recover(NoApplicationRunning);
                // Windows removes the !RunOnce value after execution. Do not rewrite
                // RunOnce from inside its callback; leave a failed journal for retry.
                return 0;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
                ArgumentException or JsonException or System.ComponentModel.Win32Exception)
            { return 4; }
        }

        if (!args.Contains("--apply-update", StringComparer.Ordinal))
        {
            return 2;
        }

        try
        {
            return ApplyUpdate(UpdateArguments.Parse(args));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidOperationException or ArgumentException or JsonException or System.Security.SecurityException)
        {
            return 3;
        }
    }

    private static int ApplyUpdate(UpdateArguments update)
    {
        ValidatePaths(update);
        string expectedVersion = UpdateHealthConfirmation.ExpectedVersion(update.StageDirectory);
        using UpdateRecoveryJournal? journal = PrepareRecoveryOrRestart(update);
        if (journal is null) { return 4; }
        WaitForParent(update.ParentProcessId);
        try { Update.UpdateInstallAvailability.EnsureAvailable(update.InstallDirectory); }
        catch (Update.UpdateException exception)
        {
            // A client may appear after the in-app preflight. Leave the install intact
            // and reopen it with an inline recovery notice instead of killing its server.
            using Process restored = StartApplication(update.InstallDirectory, null, null, sharedAdbBlockedReason: exception.ReasonCode);
            journal.Finish();
            UpdateRecoveryStartup.Remove(update.Token);
            return 4;
        }
        Process? updatedProcess = null;
        try
        {
            int result = UpdateDirectoryTransaction.Apply(
                update.InstallDirectory,
                update.StageDirectory,
                update.Token,
                () =>
                {
                    updatedProcess = StartApplication(
                        update.InstallDirectory, update.HealthFilePath, update.Token);
                    return WaitForHealth(updatedProcess, update.HealthFilePath, update.Token, expectedVersion);
                },
                () => TryStop(updatedProcess),
                () =>
                {
                    using Process restored = StartApplication(update.InstallDirectory, null, null,
                        sharedAdbBlockedReason: journal.FailureReason);
                }, journal);
            if (journal.IsStable())
            {
                journal.Finish();
                UpdateRecoveryStartup.Remove(update.Token);
            }
            return result;
        }
        finally
        {
            updatedProcess?.Dispose();
        }
    }

    private static UpdateRecoveryJournal? PrepareRecoveryOrRestart(UpdateArguments update)
    {
        try
        {
            return UpdateRecoveryStartup.Prepare(RunnerDirectory(), update.InstallDirectory, update.StageDirectory, update.Token,
                () => UpdateRecoveryStartup.Register(Environment.ProcessPath!, update.Token));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            UpdateRecoveryStartup.Remove(update.Token);
            WaitForParent(update.ParentProcessId);
            using Process restored = StartApplication(update.InstallDirectory, null, null,
                sharedAdbBlockedReason: Update.UpdateReasonCodes.RecoveryUnavailable);
            return null;
        }
    }

    private static string RunnerDirectory() => Path.GetDirectoryName(Environment.ProcessPath)
        ?? throw new IOException("Maintenance runner directory is unavailable.");

    private static bool NoApplicationRunning()
    {
        bool running = false;
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppIdentity.ExecutableName)))
        { using (process) { if (!process.HasExited) { running = true; } } }
        return !running;
    }

    private static void WaitForParent(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!process.WaitForExit(30_000))
            {
                throw new InvalidOperationException("Main application did not exit in time.");
            }
        }
        catch (ArgumentException)
        {
        }
    }

    private static Process StartApplication(
        string installDirectory,
        string? healthFile,
        string? token,
        string? sharedAdbBlockedReason = null)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(installDirectory, AppIdentity.ExecutableName),
            WorkingDirectory = installDirectory,
            UseShellExecute = false,
        };
        if (sharedAdbBlockedReason is not null)
        {
            startInfo.ArgumentList.Add("--update-adb-blocked");
            startInfo.ArgumentList.Add(sharedAdbBlockedReason);
        }
        if (!string.IsNullOrWhiteSpace(healthFile) && !string.IsNullOrWhiteSpace(token))
        {
            startInfo.ArgumentList.Add("--update-health-file");
            startInfo.ArgumentList.Add(healthFile);
            startInfo.ArgumentList.Add("--update-health-token");
            startInfo.ArgumentList.Add(token);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException(
            "Application process did not start.");
    }

    private static bool WaitForHealth(Process process, string healthFile, string token, string expectedVersion)
    {
        long deadline = Environment.TickCount64 + 45_000;
        while (Environment.TickCount64 < deadline)
        {
            if (process.HasExited)
            {
                return false;
            }

            if (UpdateHealthConfirmation.Matches(healthFile, token, expectedVersion, process.Id) && !process.HasExited)
            {
                return true;
            }

            if (process.WaitForExit(250))
            {
                return false;
            }
        }

        return false;
    }

    private static void TryStop(Process? process)
    {
        if (process is null)
        {
            return;
        }

        using (process)
        {
            try
            {
                if (!process.HasExited)
                {
                    // The application's descendants may include a now-shared ADB daemon.
                    process.Kill(entireProcessTree: false);
                    process.WaitForExit(5_000);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static void ValidatePaths(UpdateArguments update)
    {
        string install = Path.GetFullPath(update.InstallDirectory)
            .TrimEnd(Path.DirectorySeparatorChar);
        string stage = Path.GetFullPath(update.StageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar);
        string? parent = Directory.GetParent(install)?.FullName;
        if (string.IsNullOrWhiteSpace(parent) ||
            string.Equals(
                install,
                Path.GetPathRoot(install)?.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(install, AppIdentity.ExecutableName)) ||
            !File.Exists(Path.Combine(stage, AppIdentity.ExecutableName)) ||
            !File.Exists(Path.Combine(stage, "app", AppIdentity.MaintenanceExecutableName)))
        {
            throw new InvalidOperationException("Update paths are invalid.");
        }

        ValidateTransactionDirectory(stage, parent, "stage", update.Token);
    }

    private static void ValidateTransactionDirectory(
        string path,
        string parentDirectory,
        string kind,
        string token)
    {
        string expected = Path.Combine(
            Path.GetFullPath(parentDirectory),
            $".{AppIdentity.LocalDataFolderName}-{kind}-{token}");
        if (!string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Update transaction path is invalid.");
        }
    }

    private sealed record UpdateArguments(
        int ParentProcessId,
        string InstallDirectory,
        string StageDirectory,
        string HealthFilePath,
        string Token)
    {
        public static UpdateArguments Parse(IReadOnlyList<string> args) => new(
            int.Parse(Value(args, "--parent-pid"), System.Globalization.CultureInfo.InvariantCulture),
            Path.GetFullPath(Value(args, "--install-dir")),
            Path.GetFullPath(Value(args, "--stage-dir")),
            Path.GetFullPath(Value(args, "--health-file")),
            ValidateToken(Value(args, "--token")));

        private static string Value(IReadOnlyList<string> args, string name)
        {
            for (int index = 0; index < args.Count - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.Ordinal))
                {
                    return args[index + 1];
                }
            }

            throw new ArgumentException($"Missing {name}.");
        }

        private static string ValidateToken(string token)
        {
            if (token.Length != 32 || !token.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("Update token is invalid.");
            }

            return token.ToLowerInvariant();
        }
    }
}
