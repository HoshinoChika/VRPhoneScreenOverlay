using System.Security.Cryptography;

namespace VRPhoneScreenOverlay.Android;

internal sealed record AndroidResourceValidationResult(
    bool Succeeded,
    string ReasonCode,
    string Message,
    string ExecutablePath);

internal static class AndroidResourceValidator
{
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
        foreach ((string fileName, string expectedHash) in _expectedHashes)
        {
            string path = Path.Combine(fullDirectory, fileName);
            if (!File.Exists(path))
            {
                return new AndroidResourceValidationResult(
                    false,
                    AndroidReasonCodes.ResourceMissing,
                    $"缺少手机连接组件：{fileName}",
                    Path.Combine(fullDirectory, "adb.exe"));
            }

            await using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                useAsync: true);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            string actualHash = Convert.ToHexStringLower(hash);
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            {
                return new AndroidResourceValidationResult(
                    false,
                    AndroidReasonCodes.ResourceHashMismatch,
                    $"手机连接组件校验失败：{fileName}",
                    Path.Combine(fullDirectory, "adb.exe"));
            }
        }

        return new AndroidResourceValidationResult(
            true,
            AndroidReasonCodes.ResourcesReady,
            "内置手机连接组件校验通过",
            Path.Combine(fullDirectory, "adb.exe"));
    }
}
