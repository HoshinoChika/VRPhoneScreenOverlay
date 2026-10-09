namespace VRPhoneScreenOverlay.Protocols;

public readonly record struct ProtocolVersion(int Major, int Minor)
{
    public static ProtocolVersion V1 { get; } = new(1, 0);

    public override string ToString() => $"{Major}.{Minor}";
}

public sealed record SessionHello(
    ProtocolVersion Protocol,
    string ApplicationVersion,
    IReadOnlyList<string> Capabilities);
