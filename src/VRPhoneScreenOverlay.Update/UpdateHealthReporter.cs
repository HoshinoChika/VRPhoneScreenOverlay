using System.Text.Json;

namespace VRPhoneScreenOverlay.Update;

public static class UpdateHealthReporter
{
    public static void ReportHealthy(string? healthFilePath, string? token, string version)
    {
        if (string.IsNullOrWhiteSpace(healthFilePath) || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        string fullPath = Path.GetFullPath(healthFilePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        string temporary = fullPath + ".tmp";
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            token,
            version,
            processId = Environment.ProcessId,
            healthyAt = DateTimeOffset.UtcNow,
        });
        File.WriteAllBytes(temporary, content);
        File.Move(temporary, fullPath, overwrite: true);
    }
}
