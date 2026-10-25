using System.Security.Cryptography;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal sealed record AndroidResourceValidationResult(
    bool Succeeded,
    string ReasonCode,
    string Message,
    string ExecutablePath)
{
    public ComponentFileEvidence[] Files { get; init; } = [];
}

internal static class AndroidResourceValidator
{
    internal static IReadOnlyDictionary<string, string> ExpectedHashes => _expectedHashes;
    private static readonly IReadOnlyDictionary<string, string> _expectedHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["adb.exe"] = "957e46b8615f7af5b7292a2ddabe98d2e61940c3fb2b0545756507f080613e71",
            ["AdbWinApi.dll"] = "120bef587119c6cb926b86b9be90fdfbce38937588eae28cd91a94ce63c7b965",
            ["AdbWinUsbApi.dll"] = "6ca69a2ca0e31309c087d288f058977d421ad03500e4c3e1dbd981241a069c60",
        };

    public static async ValueTask<AndroidResourceValidationResult> ValidateAsync(
        string resourceDirectory,
        CancellationToken cancellationToken)
    {
        string fullDirectory = Path.GetFullPath(resourceDirectory);
        List<ComponentFileEvidence> files = [];
        foreach ((string fileName, string expectedHash) in _expectedHashes)
        {
            string path = Path.Combine(fullDirectory, fileName);
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                { files.Add(new(fileName, "redirected", null, expectedHash, null)); continue; }
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 128 * 1024, useAsync: true);
                long size = stream.Length;
                if (size > 32L * 1024 * 1024)
                { files.Add(new(fileName, "size-limit", size, expectedHash, null)); continue; }
                string actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
                files.Add(new(fileName, actualHash == expectedHash ? "valid" : "hash-mismatch", size, expectedHash, actualHash));
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
            { files.Add(new(fileName, "missing", null, expectedHash, null)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { files.Add(new(fileName, "unreadable", null, expectedHash, null, error.HResult)); }
        }
        ComponentFileEvidence? failed = files.FirstOrDefault(f => f.Status != "valid");
        return failed is null
            ? new(true, AndroidReasonCodes.ResourcesReady, "手机连接组件校验通过", Path.Combine(fullDirectory, "adb.exe")) { Files = files.ToArray() }
            : new(false, failed.Status == "missing" ? AndroidReasonCodes.ResourceMissing : AndroidReasonCodes.ResourceHashMismatch,
                $"手机连接组件校验失败：{failed.File}", Path.Combine(fullDirectory, "adb.exe"))
            { Files = files.ToArray() };
    }
}
