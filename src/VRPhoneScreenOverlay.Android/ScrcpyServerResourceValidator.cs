using System.Security.Cryptography;
using System.Text.Json;

namespace VRPhoneScreenOverlay.Android;

internal static class ScrcpyServerResourceValidator
{
    public static async ValueTask<string> ValidateAsync(
        string serverPath,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(serverPath);
        if (!File.Exists(fullPath))
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.ScrcpyServerMissing,
                "缺少 scrcpy 手机服务组件");
        }

        string? directory = Path.GetDirectoryName(fullPath);
        string manifestPath = Path.Combine(
            directory ?? throw new AndroidConnectionException(
                AndroidReasonCodes.ScrcpyManifestMissing,
                "缺少 scrcpy 手机服务清单"),
            "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.ScrcpyManifestMissing,
                "缺少 scrcpy 手机服务清单");
        }

        string expectedHash = await ReadExpectedHashAsync(
                manifestPath,
                Path.GetFileName(fullPath),
                cancellationToken)
            .ConfigureAwait(false);

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
            Convert.ToHexStringLower(hash),
            expectedHash,
            StringComparison.Ordinal))
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.ScrcpyServerHashMismatch,
                "scrcpy 手机服务组件校验失败");
        }

        return fullPath;
    }

    private static async ValueTask<string> ReadExpectedHashAsync(
        string manifestPath,
        string serverFileName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                useAsync: true);
            using JsonDocument document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("customization", out JsonElement customization) ||
                customization.ValueKind != JsonValueKind.Object ||
                !customization.TryGetProperty("guardedWakeControlMessageType", out JsonElement guardedWake) ||
                guardedWake.ValueKind != JsonValueKind.Number ||
                !guardedWake.TryGetInt32(out int messageType) || messageType != 128)
            {
                throw new JsonException("The scrcpy server does not declare the required guarded-wake extension.");
            }
            if (!document.RootElement.TryGetProperty("files", out JsonElement files) ||
                files.ValueKind != JsonValueKind.Object ||
                !files.TryGetProperty(serverFileName, out JsonElement hashElement) ||
                hashElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("The scrcpy server hash is missing from the manifest.");
            }

            string? expectedHash = hashElement.GetString();
            if (expectedHash is null || expectedHash.Length != 64 ||
                !expectedHash.All(Uri.IsHexDigit))
            {
                throw new JsonException("The scrcpy server hash in the manifest is invalid.");
            }

            return expectedHash.ToLowerInvariant();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.ScrcpyManifestInvalid,
                "scrcpy 手机服务清单无效");
        }
    }
}
