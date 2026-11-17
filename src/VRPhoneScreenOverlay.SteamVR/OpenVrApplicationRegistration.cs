using System.Text;
using System.Text.Json;
using Valve.VR;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

internal interface IOpenVrApplicationRegistry
{
    public (string Value, EVRApplicationError Error) Read(EVRApplicationProperty property);
    public EVRApplicationError Add(string manifest);
    public EVRApplicationError Remove(string manifest);
    public EVRApplicationError SetAutoLaunch(bool enabled);
    public bool GetAutoLaunch();
}

internal static class OpenVrApplicationRegistration
{
    internal static SteamVrStartupDiagnostic Apply(string manifestPath, bool? enabled, IOpenVrApplicationRegistry registry,
        bool runtimeOnly = false)
    {
        SteamVrStartupDiagnostic evidence = new("manifest", RequestedAutoLaunch: enabled);
        try
        {
            string manifest = Path.GetFullPath(manifestPath);
            evidence = evidence with { ManifestExists = File.Exists(manifest) };
            if (!evidence.ManifestExists || new FileInfo(manifest).Length > 1_048_576) { return evidence; }
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
            JsonElement app = document.RootElement.GetProperty("applications").EnumerateArray()
                .Single(entry => entry.GetProperty("app_key").GetString() == OpenVrInputManifest.ApplicationKey);
            string root = Path.GetDirectoryName(manifest)!;
            string binary = app.GetProperty("binary_path_windows").GetString()!;
            if (binary is not "VRPhoneScreenOverlay.exe" and not "../VRPhoneScreenOverlay.exe") { return evidence; }
            if (binary.StartsWith("../", StringComparison.Ordinal) && !string.Equals(Path.GetFileName(root), "app", StringComparison.OrdinalIgnoreCase))
            { return evidence; }
            string expected = Path.GetFullPath(Path.Combine(root, binary));
            evidence = evidence with { Stage = "executable", ExecutableExists = File.Exists(expected) };
            if (!evidence.ExecutableExists) { return evidence; }

            // A running overlay needs its application identity, not installation relocation
            // or auto-launch verification. Keep the strict maintenance path separate.
            if (runtimeOnly)
            {
                EVRApplicationError runtimeAdded = registry.Add(manifest);
                return evidence with
                {
                    Stage = runtimeAdded == EVRApplicationError.None ? "confirmed" : "add",
                    NativeError = runtimeAdded == EVRApplicationError.None ? null : runtimeAdded.ToString()
                };
            }

            string? previous = ReadBinary(registry, out _);
            string? previousDirectory = previous is null ? null : Path.GetDirectoryName(previous);
            if (previousDirectory is not null)
            {
                List<string> errors = [];
                foreach (string relative in new[] { "manifest.vrmanifest", "app/manifest.vrmanifest" })
                {
                    string old = Path.GetFullPath(Path.Combine(previousDirectory, relative));
                    if (string.Equals(old, manifest, StringComparison.OrdinalIgnoreCase)) { continue; }
                    EVRApplicationError removal = registry.Remove(old);
                    evidence = evidence with
                    {
                        OldManifestAttempts = evidence.OldManifestAttempts + 1,
                        OldManifestRemoved = evidence.OldManifestRemoved + (removal == EVRApplicationError.None ? 1 : 0)
                    };
                    if (removal != EVRApplicationError.None) { errors.Add(removal.ToString()); }
                }
                evidence = evidence with { RemovalErrors = errors.Count == 0 ? null : string.Join(',', errors) };
            }
            evidence = evidence with { Stage = "add" };
            EVRApplicationError added = registry.Add(manifest);
            if (added != EVRApplicationError.None) { return evidence with { NativeError = added.ToString() }; }
            string? registeredBinary = ReadBinary(registry, out string? readFailure);
            evidence = evidence with
            {
                Stage = "path-readback",
                RegisteredPathMatches = string.Equals(registeredBinary, expected, StringComparison.OrdinalIgnoreCase),
                NativeError = readFailure
            };
            if (!evidence.RegisteredPathMatches) { return evidence; }
            if (enabled is bool requested)
            {
                evidence = evidence with { Stage = "set-autolaunch" };
                EVRApplicationError set = registry.SetAutoLaunch(requested);
                if (set != EVRApplicationError.None) { return evidence with { NativeError = set.ToString() }; }
            }
            evidence = evidence with { Stage = "autolaunch-readback", ObservedAutoLaunch = registry.GetAutoLaunch() };
            if (enabled.HasValue && evidence.ObservedAutoLaunch != enabled) { return evidence; }
            return evidence with { Stage = "confirmed" };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or ArgumentException or KeyNotFoundException)
        { return evidence with { NativeError = error.GetType().Name }; }
    }

    private static string? ReadBinary(IOpenVrApplicationRegistry registry, out string? failure)
    {
        failure = null;
        (string binary, EVRApplicationError error) = registry.Read(EVRApplicationProperty.BinaryPath_String);
        if (error != EVRApplicationError.None) { failure = error.ToString(); return null; }
        if (string.IsNullOrWhiteSpace(binary)) { failure = "EMPTY_BINARY_PATH"; return null; }
        if (!Path.IsPathFullyQualified(binary))
        {
            (string working, EVRApplicationError directoryError) = registry.Read(EVRApplicationProperty.WorkingDirectory_String);
            if (directoryError != EVRApplicationError.None) { failure = directoryError.ToString(); return null; }
            if (!Path.IsPathFullyQualified(working)) { failure = "INVALID_WORKING_DIRECTORY"; return null; }
            binary = Path.Combine(working, binary);
        }
        return Path.GetFullPath(binary);
    }
}

internal sealed class OpenVrApplicationRegistry : IOpenVrApplicationRegistry
{
    public (string Value, EVRApplicationError Error) Read(EVRApplicationProperty property)
    {
        EVRApplicationError error = EVRApplicationError.None;
        StringBuilder value = new(4096);
        OpenVR.Applications.GetApplicationPropertyString(OpenVrInputManifest.ApplicationKey, property, value, (uint)value.Capacity, ref error);
        return (value.ToString(), error);
    }
    public EVRApplicationError Add(string manifest) => OpenVR.Applications.AddApplicationManifest(manifest, false);
    public EVRApplicationError Remove(string manifest) => OpenVR.Applications.RemoveApplicationManifest(manifest);
    public EVRApplicationError SetAutoLaunch(bool enabled) => OpenVR.Applications.SetApplicationAutoLaunch(OpenVrInputManifest.ApplicationKey, enabled);
    public bool GetAutoLaunch() => OpenVR.Applications.GetApplicationAutoLaunch(OpenVrInputManifest.ApplicationKey);
}
