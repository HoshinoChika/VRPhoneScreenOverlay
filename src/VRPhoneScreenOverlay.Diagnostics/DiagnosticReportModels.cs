using System.Text.Json.Serialization;

namespace VRPhoneScreenOverlay.Diagnostics;

[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticIssueType>))]
public enum DiagnosticIssueType
{
    Video,
    Audio,
    Control,
    DeviceConnection,
    SteamVrBinding,
    PlayspaceDrag,
    Update,
    Other,
}

public sealed record DiagnosticUserReport(
    DiagnosticIssueType IssueType,
    DateTimeOffset OccurredAt,
    string? Description);

public sealed record DiagnosticHardwareSummary(
    string? AndroidManufacturer,
    string? AndroidBrand,
    string? AndroidModel,
    string? AndroidDeviceCodeName,
    string? AndroidVersion,
    int? AndroidSdk,
    string? AndroidCpuAbi,
    string? AndroidTransport,
    int NativeDisplayWidth,
    int NativeDisplayHeight,
    string? VideoDecoderBackend,
    string? GraphicsAdapterBackend,
    long OpenVrGraphicsAdapterLuid,
    string? HeadsetModel,
    string? ControllerType);

public sealed record DiagnosticSessionSummary(
    DateTimeOffset CapturedAt,
    string MediaState,
    string MediaReasonCode,
    string MediaMessage,
    string VideoState,
    string VideoReasonCode,
    string VideoMessage,
    int VideoWidth,
    int VideoHeight,
    long SubmittedFrames,
    long DroppedFrames,
    double SubmittedFramesPerSecond,
    int RequestedResolutionPercent,
    int RequestedBitrateMbps,
    int RequestedMaximumFramesPerSecond,
    double AverageBitrateMbps,
    double PeakBitrateMbps,
    double AverageLatencyMilliseconds,
    string AudioState,
    string AudioReasonCode,
    string AudioMessage,
    long DecodedAudioPackets,
    long AudioBufferResetCount,
    double AudioBufferedMilliseconds,
    bool AudioOutputDeviceAvailable,
    string ControlState,
    string ControlReasonCode,
    string ControlMessage,
    long SentControlCommands,
    long ReplacedPointerMoves,
    double LastControlQueueDelayMilliseconds,
    string PlayspaceState,
    string PlayspaceReasonCode,
    string PlayspaceMessage,
    bool PlayspaceEnabled,
    float PlayspaceMultiplier,
    float PlayspaceOffsetX,
    float PlayspaceOffsetY,
    float PlayspaceOffsetZ);

public sealed record DiagnosticBindingProfileSummary(
    string ControllerType,
    string Source);

public sealed record DiagnosticBindingSummary(
    bool Prepared,
    string? Revision,
    string ControllerHand,
    IReadOnlyList<DiagnosticBindingProfileSummary> Profiles,
    string BindingState,
    string BindingReasonCode,
    string BindingMessage,
    string? ActiveControllerType,
    bool SteamVrInputReady,
    bool PointerPoseBound,
    bool PointerPoseActive,
    bool TouchBound,
    bool TouchActive,
    bool GrabBound,
    bool GrabActive,
    bool ScaleBound,
    bool ScaleActive);
