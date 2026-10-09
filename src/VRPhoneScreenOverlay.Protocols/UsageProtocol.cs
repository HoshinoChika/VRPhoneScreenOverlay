namespace VRPhoneScreenOverlay.Protocols;

public sealed record UsageHeartbeatRequest(int SchemaVersion, string InstallationId);

public sealed record UsageStatistics(
    int TotalInstallations,
    int Active24Hours,
    int Active7Days,
    int Active30Days,
    int Online,
    DateTimeOffset AsOf,
    int OnlineTimeoutSeconds);
