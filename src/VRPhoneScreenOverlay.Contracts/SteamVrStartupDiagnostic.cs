namespace VRPhoneScreenOverlay.Contracts;

public sealed record SteamVrStartupDiagnostic(string Stage, bool ManifestExists = false,
    bool ExecutableExists = false, bool RegisteredPathMatches = false, int OldManifestAttempts = 0,
    int OldManifestRemoved = 0, string? RemovalErrors = null, bool? RequestedAutoLaunch = null,
    bool? ObservedAutoLaunch = null, string? NativeError = null);
