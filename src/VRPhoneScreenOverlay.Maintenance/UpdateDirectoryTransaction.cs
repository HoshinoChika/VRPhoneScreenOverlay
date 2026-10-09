using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Maintenance;

internal static class UpdateDirectoryTransaction
{
    public static int Apply(
        string installDirectory,
        string stageDirectory,
        string token,
        Func<bool> startAndCheckHealth,
        Action stopUpdatedApplication,
        Action restartRestoredApplication)
    {
        ArgumentNullException.ThrowIfNull(startAndCheckHealth);
        ArgumentNullException.ThrowIfNull(stopUpdatedApplication);
        ArgumentNullException.ThrowIfNull(restartRestoredApplication);
        if (token.Length != 32 || !token.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Update token is invalid.", nameof(token));
        }

        string install = Path.GetFullPath(installDirectory);
        string parent = Directory.GetParent(install)?.FullName ??
            throw new ArgumentException("Update installation path is invalid.", nameof(installDirectory));
        string stage = TransactionPath(parent, "stage", token);
        if (!string.Equals(stage, Path.GetFullPath(stageDirectory), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Update stage path is invalid.", nameof(stageDirectory));
        }

        string backup = TransactionPath(parent, "backup", token);
        string failed = TransactionPath(parent, "failed", token);
        bool installMoved = false;
        try
        {
            Directory.Move(install, backup);
            installMoved = true;
            Directory.Move(stage, install);
            if (!startAndCheckHealth())
            {
                throw new InvalidOperationException("Updated application did not report health.");
            }
        }
        catch
        {
            stopUpdatedApplication();
            if (installMoved && Directory.Exists(backup))
            {
                if (Directory.Exists(install))
                {
                    Directory.Move(install, failed);
                }

                Directory.Move(backup, install);
                restartRestoredApplication();
                TryDeleteTransactionDirectory(failed);
            }

            return 4;
        }

        // Health commits the installation. Recursive cleanup can partially delete
        // a backup before throwing, so it must never trigger installation rollback.
        TryDeleteTransactionDirectory(backup);
        return 0;
    }

    private static string TransactionPath(string parent, string kind, string token) =>
        Path.Combine(parent, $".{AppIdentity.LocalDataFolderName}-{kind}-{token}");

    private static void TryDeleteTransactionDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Retain any remainder for later cleanup; the healthy/restored app owns
            // the installed directory and must remain running when cleanup fails.
        }
    }
}
