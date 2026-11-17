using System.Diagnostics;
using System.Text.Json;

namespace VRPhoneScreenOverlay.Maintenance;

internal static class UpdateHealthConfirmation
{
    private const int _maximumBytes = 4096;

    internal static string ExpectedVersion(string stageDirectory)
    {
        string? product = FileVersionInfo.GetVersionInfo(Path.Combine(stageDirectory, "app", "VRPhoneScreenOverlay.dll")).ProductVersion;
        if (string.IsNullOrWhiteSpace(product)) { throw new InvalidOperationException("Staged application version is unavailable."); }
        // Match UpdateHealthReporter's caller: build metadata identifies the build,
        // while the health report carries the application's product version.
        return product.Split('+', 2)[0];
    }

    internal static bool Matches(string path, string token, string version, int processId)
    {
        try
        {
            using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] bytes = new byte[_maximumBytes + 1];
            int count = 0;
            while (count < bytes.Length)
            {
                int read = input.Read(bytes, count, bytes.Length - count);
                if (read == 0) { break; }
                count += read;
            }
            if (count > _maximumBytes) { return false; }
            using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(0, count));
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("token", out JsonElement actualToken) && actualToken.ValueKind == JsonValueKind.String &&
                string.Equals(actualToken.GetString(), token, StringComparison.Ordinal) &&
                root.TryGetProperty("version", out JsonElement actualVersion) && actualVersion.ValueKind == JsonValueKind.String &&
                string.Equals(actualVersion.GetString(), version, StringComparison.Ordinal) &&
                root.TryGetProperty("processId", out JsonElement actualProcess) && actualProcess.ValueKind == JsonValueKind.Number &&
                actualProcess.TryGetInt32(out int reportedProcess) && reportedProcess == processId;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return false; }
    }
}
