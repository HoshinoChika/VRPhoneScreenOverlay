namespace VRPhoneScreenOverlay.Android;

public enum AndroidConnectionState
{
    Created,
    Discovering,
    WaitingForDevice,
    AuthorizationRequired,
    Offline,
    Connecting,
    Ready,
    Reconnecting,
    Faulted,
    Stopping,
    Stopped,
}

public enum AndroidDeviceStatus
{
    Ready,
    Unauthorized,
    Offline,
    NoPermissions,
    Unknown,
}

public enum AndroidTransport
{
    Usb,
    Network,
    Emulator,
    Unknown,
}

public enum AndroidCapabilityState
{
    Unknown,
    Available,
    Unavailable,
}

public readonly record struct AndroidCapability(
    AndroidCapabilityState State,
    string ReasonCode,
    string Message)
{
    public bool IsAvailable => State == AndroidCapabilityState.Available;
}

public sealed record AndroidDeviceCapabilities(
    AndroidCapability Video,
    AndroidCapability Control,
    AndroidCapability InternalAudio)
{
    public static AndroidDeviceCapabilities Unknown { get; } = new(
        new(AndroidCapabilityState.Unknown, AndroidReasonCodes.VideoCapabilityUnknown, "尚未读取 Android SDK 版本"),
        new(AndroidCapabilityState.Unknown, AndroidReasonCodes.ControlCapabilityUnknown, "尚未读取 Android SDK 版本"),
        new(AndroidCapabilityState.Unknown, AndroidReasonCodes.AudioCapabilityUnknown, "尚未读取 Android SDK 版本"));
}

public sealed record AndroidDeviceView(
    string DeviceKey,
    string DisplayName,
    string Model,
    AndroidDeviceStatus Status,
    AndroidTransport Transport,
    bool IsSelected,
    string AndroidVersion = "")
{
    public string? PhysicalDeviceKey { get; init; }
}

public sealed record AndroidDeviceDetails(
    string DeviceKey,
    string Manufacturer,
    string Brand,
    string Model,
    string DeviceCodeName,
    string AndroidVersion,
    int? AndroidSdk,
    string CpuAbi,
    AndroidTransport Transport)
{
    public AndroidDeviceCapabilities Capabilities { get; init; } = AndroidDeviceCapabilities.Unknown;

    public int NativeDisplayWidth { get; init; }

    public int NativeDisplayHeight { get; init; }
}

public sealed record AndroidConnectionSnapshot(
    long Revision,
    AndroidConnectionState State,
    string ReasonCode,
    string Message,
    IReadOnlyList<AndroidDeviceView> Devices,
    AndroidDeviceDetails? SelectedDevice,
    DateTimeOffset ChangedAt,
    DateTimeOffset? LastSuccessfulScanAt)
{
    public bool IsReady => State == AndroidConnectionState.Ready;
}

public sealed class AndroidConnectionChangedEventArgs(AndroidConnectionSnapshot snapshot) : EventArgs
{
    public AndroidConnectionSnapshot Snapshot { get; } = snapshot;
}

public readonly record struct AndroidOperationResult(
    bool Succeeded,
    string ReasonCode,
    string Message)
{
    internal string? ConnectedEndpoint { get; init; }
    public override string ToString() => $"{ReasonCode}: {Message}";
}

public interface IAndroidConnectionService : IAsyncDisposable
{
    public AndroidConnectionSnapshot Snapshot { get; }

    public event EventHandler<AndroidConnectionChangedEventArgs>? StateChanged;

    public ValueTask StartAsync(CancellationToken cancellationToken);

    public ValueTask RefreshAsync(CancellationToken cancellationToken);

    public ValueTask SelectDeviceAsync(string deviceKey, CancellationToken cancellationToken);

}
