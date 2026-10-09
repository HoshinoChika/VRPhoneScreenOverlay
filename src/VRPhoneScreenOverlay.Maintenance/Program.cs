using System.Diagnostics;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Maintenance;

internal static class Program
{
    private static int Main(string[] args)
    {
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
                InvalidOperationException or ArgumentException or JsonException)
        {
            return 3;
        }
    }

    private static int ApplyUpdate(UpdateArguments update)
    {
        ValidatePaths(update);
        WaitForParent(update.ParentProcessId);
        InstallDirectoryLockReleaser.Release(update.InstallDirectory);
        Process? updatedProcess = null;
        try
        {
            return UpdateDirectoryTransaction.Apply(
                update.InstallDirectory,
                update.StageDirectory,
                update.Token,
                () =>
                {
                    updatedProcess = StartApplication(
                        update.InstallDirectory, update.HealthFilePath, update.Token);
                    return WaitForHealth(updatedProcess, update.HealthFilePath, update.Token);
                },
                () => TryStop(updatedProcess),
                () =>
                {
                    using Process restored = StartApplication(update.InstallDirectory, null, null);
                });
        }
        finally
        {
            updatedProcess?.Dispose();
        }
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
        string? token)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Path.Combine(installDirectory, AppIdentity.ExecutableName),
            WorkingDirectory = installDirectory,
            UseShellExecute = false,
        };
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

    private static bool WaitForHealth(Process process, string healthFile, string token)
    {
        long deadline = Environment.TickCount64 + 45_000;
        while (Environment.TickCount64 < deadline)
        {
            if (process.HasExited)
            {
                return false;
            }

            if (File.Exists(healthFile))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(healthFile));
                    if (document.RootElement.TryGetProperty("token", out JsonElement value) &&
                        string.Equals(value.GetString(), token, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                }
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
                    process.Kill(entireProcessTree: true);
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
