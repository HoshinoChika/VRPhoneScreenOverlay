using System.IO.Compression;
using System.Security.Cryptography;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Network;

namespace VRPhoneScreenOverlay.Update;

internal sealed record UpdateStagingPaths(string DownloadRoot, string InstallDirectory);

internal static class UpdatePackageStager
{
    public static async ValueTask<PreparedUpdate> DownloadAndStageAsync(
        IAppHttpClient httpClient,
        UpdateRelease release,
        long maximumPackageBytes,
        TimeSpan responseIdleTimeout,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken,
        UpdateStagingPaths? paths = null)
    {
        string token = Guid.NewGuid().ToString("N");
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string updateRoot = paths?.DownloadRoot ?? Path.Combine(localData, AppIdentity.LocalDataFolderName, "updates");
        string downloadDirectory = Path.Combine(updateRoot, "downloads");
        Directory.CreateDirectory(downloadDirectory);
        string downloadPath = Path.Combine(downloadDirectory, $"{token}.zip");
        string temporaryDownload = downloadPath + ".tmp";
        string? stageDirectory = null;
        string? runnerDirectory = null;
        try
        {
            // Downloading the package is a pure read into a fresh temp file, so a transient
            // failure may be retried from the start.
            using HttpResponseMessage response = await OpenPackageAsync(httpClient, release, cancellationToken);
            long contentLength = response.Content.Headers.ContentLength ?? release.PackageSize;
            if (contentLength != release.PackageSize || contentLength > maximumPackageBytes)
            {
                throw new UpdateException(
                    UpdateReasonCodes.PackageSizeInvalid,
                    "更新包大小与清单不一致");
            }

            long received = 0;
            await using (Stream source = await NetworkResponseBody.OpenDownloadAsync(
                             response.Content, responseIdleTimeout, cancellationToken)
                             .ConfigureAwait(false))
            await using (FileStream destination = new(
                             temporaryDownload,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[128 * 1024];
                while (true)
                {
                    int read = await source.ReadAsync(buffer, cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    received = checked(received + read);
                    if (received > release.PackageSize || received > maximumPackageBytes)
                    {
                        throw new UpdateException(
                            UpdateReasonCodes.PackageTooLarge,
                            "更新包超过允许大小");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    progress?.Report(new UpdateDownloadProgress(received, release.PackageSize));
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (received != release.PackageSize)
            {
                throw new UpdateException(UpdateReasonCodes.PackageTruncated, "更新包下载不完整");
            }

            string actualHash;
            await using (FileStream stream = File.OpenRead(temporaryDownload))
            {
                actualHash = Convert.ToHexStringLower(
                    await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            }

            if (!string.Equals(actualHash, release.PackageSha256, StringComparison.Ordinal))
            {
                throw new UpdateException(UpdateReasonCodes.PackageHashMismatch, "更新包校验失败");
            }

            File.Move(temporaryDownload, downloadPath);
            string installDirectory = paths?.InstallDirectory ?? UpdateInstallLayout.ResolveInstallDirectory(
                AppContext.BaseDirectory, Environment.ProcessPath);
            ValidateInstallDirectory(installDirectory);
            string installParent = Directory.GetParent(installDirectory)?.FullName ??
                throw new UpdateException(UpdateReasonCodes.InstallPathInvalid, "软件安装目录无效");
            stageDirectory = Path.Combine(
                installParent,
                $".{AppIdentity.LocalDataFolderName}-stage-{token}");
            Directory.CreateDirectory(stageDirectory);
            ExtractPackage(downloadPath, stageDirectory);
            try
            {
                UpdateInstallLayout.PrepareFullPackageStage(stageDirectory);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
            {
                throw new UpdateException(UpdateReasonCodes.PackageLayoutInvalid, "全量更新包目录结构无效", exception);
            }

            ValidateInstallDirectory(stageDirectory);
            UpdateInstallLayout.PreserveClientConfiguration(installDirectory, stageDirectory);

            runnerDirectory = Path.Combine(updateRoot, "runner", token);
            Directory.CreateDirectory(runnerDirectory);
            string maintenanceSource = UpdateInstallLayout.MaintenancePath(installDirectory);
            string maintenanceRunner = Path.Combine(
                runnerDirectory,
                AppIdentity.MaintenanceExecutableName);
            if (!File.Exists(maintenanceSource))
            {
                throw new UpdateException(
                    UpdateReasonCodes.MaintenanceMissing,
                    "当前安装缺少更新维护程序");
            }

            File.Copy(maintenanceSource, maintenanceRunner, overwrite: false);
            string healthFile = Path.Combine(updateRoot, "health", $"{token}.json");
            Directory.CreateDirectory(Path.GetDirectoryName(healthFile)!);
            File.Delete(downloadPath);
            return new PreparedUpdate(
                release.Version,
                stageDirectory,
                maintenanceRunner,
                installDirectory,
                healthFile,
                token);
        }
        catch
        {
            TryDeleteFile(temporaryDownload);
            TryDeleteFile(downloadPath);
            TryDeleteDirectory(stageDirectory);
            TryDeleteDirectory(runnerDirectory);
            throw;
        }
    }

    internal static async Task<HttpResponseMessage> OpenPackageAsync(IAppHttpClient client, UpdateRelease release, CancellationToken cancellationToken)
    {
        Uri address = release.DownloadExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30) && release.RefreshUri is not null
            ? release.RefreshUri : release.PackageUri;
        try
        {
            return await client.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, address),
                NetworkRetryPolicy.Idempotent, HttpCompletionOption.ResponseHeadersRead, null, cancellationToken);
        }
        catch (NetworkException exception) when (exception.StatusCode is 401 or 403 or 404 or 410 &&
            release.RefreshUri is not null && release.PackageUri != release.RefreshUri)
        {
            UriBuilder refresh = new(release.RefreshUri) { Query = "refresh=true" };
            return await client.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, refresh.Uri),
                NetworkRetryPolicy.None, HttpCompletionOption.ResponseHeadersRead, null, cancellationToken);
        }
    }

    private static void ExtractPackage(string packagePath, string stageDirectory)
    {
        string stagePrefix = Path.GetFullPath(stageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(packagePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string destination = Path.GetFullPath(Path.Combine(stageDirectory, entry.FullName));
            if (!destination.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException(
                    UpdateReasonCodes.PackagePathInvalid,
                    "更新包包含无效路径");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: false);
        }
    }

    internal static void ValidateInstallDirectory(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        string root = Path.GetPathRoot(fullPath) ?? string.Empty;
        if (string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        { throw new UpdateException(UpdateReasonCodes.InstallPathInvalid, "软件安装目录无效"); }
        try { UpdateInstallLayout.ValidateFullInstallation(fullPath); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or
            InvalidOperationException or KeyNotFoundException)
        { throw new UpdateException(UpdateReasonCodes.InstallPathInvalid, "软件安装目录无效", exception); }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
