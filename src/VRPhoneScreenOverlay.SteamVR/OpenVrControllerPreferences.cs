using System.Text.Json;

namespace VRPhoneScreenOverlay.SteamVR;

public enum OpenVrControllerHand
{
    Right,
    Left,
}

public static class OpenVrControllerPreferences
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static OpenVrControllerHand Load()
    {
        string? environment = Environment.GetEnvironmentVariable("VRPSO_CONTROLLER_HAND");
        if (Enum.TryParse(environment, true, out OpenVrControllerHand configured))
        {
            return configured;
        }

        OpenVrControllerHand? unified = LoadUnifiedSettings(GetUnifiedSettingsPath());
        return unified ?? LoadFromPath(GetLegacyPath());
    }

    internal static OpenVrControllerHand LoadFromPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return OpenVrControllerHand.Right;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            string? hand = document.RootElement.GetProperty("controllerHand").GetString();
            return Enum.TryParse(hand, true, out OpenVrControllerHand configured)
                ? configured
                : OpenVrControllerHand.Right;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
                InvalidOperationException)
        {
            return OpenVrControllerHand.Right;
        }
    }

    public static void Save(OpenVrControllerHand hand) => SaveToPath(hand, GetLegacyPath());

    internal static void SaveToPath(OpenVrControllerHand hand, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".new";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(new ControllerPreferenceDocument(hand.ToString()), _jsonOptions));
        File.Move(temporary, path, true);
    }

    private static OpenVrControllerHand? LoadUnifiedSettings(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            string? hand = document.RootElement.GetProperty("controllerHand").GetString();
            return Enum.TryParse(hand, true, out OpenVrControllerHand configured)
                ? configured
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
                InvalidOperationException)
        {
            return null;
        }
    }

    private static string GetUnifiedSettingsPath()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localData, "VRPhoneScreenOverlay", "settings.json");
    }

    private static string GetLegacyPath()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localData, "VRPhoneScreenOverlay", "input.json");
    }

    private sealed record ControllerPreferenceDocument(string ControllerHand);
}
