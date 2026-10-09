using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal sealed record DiagnosticUpdateState(DateTimeOffset? CheckedAt, string Reason, string? FailureKind, int? HttpStatus);
internal sealed record DiagnosticContextInput(
    string Version, AppRuntimeSnapshot Runtime, AppSettings Settings, AndroidConnectionSnapshot Android,
    PhoneMediaSessionSnapshot Media, PhoneOverlaySnapshot Video, PhoneOverlaySnapshot VideoDiagnostics,
    PhoneAudioSnapshot Audio, PhoneAudioSnapshot AudioDiagnostics, PhoneControlSnapshot Control,
    PhoneControlSnapshot ControlDiagnostics, OpenVrPlayspaceDragSnapshot Playspace,
    OpenVrBindingPreparationSnapshot Binding, DiagnosticUpdateState Update, DiagnosticUserReport UserReport,
    string? LogPath);

/// <summary>Privacy allowlist mapping from immutable snapshots, without UI controls or device calls.</summary>
internal static class DiagnosticContextBuilder
{
    public static DiagnosticUploadContext Build(DiagnosticContextInput input)
    {
        AppSettings settings = input.Settings;
        AndroidConnectionSnapshot android = input.Android;
        AndroidDeviceDetails? device = android.SelectedDevice;
        PhoneMediaSessionSnapshot media = input.Media;
        PhoneOverlaySnapshot video = input.Video;
        PhoneOverlaySnapshot videoDiagnostics = input.VideoDiagnostics;
        PhoneAudioSnapshot audio = input.Audio;
        PhoneAudioSnapshot audioDiagnostics = input.AudioDiagnostics;
        PhoneControlSnapshot control = input.Control;
        PhoneControlSnapshot controlDiagnostics = input.ControlDiagnostics;
        OpenVrPlayspaceDragSnapshot playspace = input.Playspace;
        OpenVrBindingPreparationSnapshot bindingPreparation = input.Binding;
        DiagnosticUserReport userReport = input.UserReport;
        return new DiagnosticUploadContext(
            input.Version,
            new Dictionary<string, object?>
            {
                ["appState"] = input.Runtime.State.ToString(),
                ["androidState"] = android.State.ToString(),
                ["androidReason"] = android.ReasonCode,
                ["videoState"] = video.State.ToString(),
                ["videoReason"] = video.ReasonCode,
                ["audioState"] = audio.State.ToString(),
                ["audioReason"] = audio.ReasonCode,
                ["controlState"] = control.State.ToString(),
                ["controlReason"] = control.ReasonCode,
                ["playspaceState"] = playspace.State.ToString(),
                ["steamVrInputReady"] = video.SteamVrInputReady,
                ["bindingState"] = video.BindingState.ToString(),
                ["lastUpdateCheckAt"] = input.Update.CheckedAt,
                ["lastUpdateReason"] = input.Update.Reason,
                ["lastUpdateFailureKind"] = input.Update.FailureKind,
                ["lastUpdateHttpStatus"] = input.Update.HttpStatus,
                ["serviceConnection"] = "direct-https",
                ["deviceCount"] = android.Devices.Count,
                ["devices"] = android.Devices.Select(item => new { item.Model, item.AndroidVersion, Transport = item.Transport.ToString(), Status = item.Status.ToString(), item.IsSelected }).ToArray(),
            },
            new Dictionary<string, object?>
            {
                ["controllerHand"] = settings.ControllerHand.ToString(),
                ["resolutionPercent"] = settings.VideoResolutionPercent,
                ["bitrateMbps"] = settings.VideoBitrateMbps,
                ["maximumFps"] = settings.VideoMaximumFramesPerSecond,
                ["keepAwakeWhileGrabbed"] = settings.KeepAwakeWhileGrabbed,
                ["vrUnlockKeypadEnabled"] = settings.VrUnlockKeypadEnabled,
                ["updateChannel"] = "Beta",
                ["playspaceFlingEnabled"] = settings.PlayspaceInertiaEnabled,
                ["playspaceFlingStrength"] = settings.PlayspaceFlingStrength,
                ["playspaceGravity"] = settings.PlayspaceGravity,
                ["playspaceFriction"] = settings.PlayspaceFriction,
                ["playspaceResetAllOffsets"] = settings.PlayspaceResetAllOffsets,
            },
            new DiagnosticHardwareSummary(
                device?.Manufacturer,
                device?.Brand,
                device?.Model,
                device?.DeviceCodeName,
                device?.AndroidVersion,
                device?.AndroidSdk,
                device?.CpuAbi,
                device?.Transport.ToString(),
                device?.NativeDisplayWidth ?? 0,
                device?.NativeDisplayHeight ?? 0,
                videoDiagnostics.VideoDecoderBackend,
                videoDiagnostics.GraphicsAdapterBackend,
                videoDiagnostics.OpenVrGraphicsAdapterLuid,
                videoDiagnostics.HeadsetModel,
                videoDiagnostics.ControllerType),
            new DiagnosticSessionSummary(
                DateTimeOffset.UtcNow,
                media.State.ToString(),
                media.ReasonCode,
                media.Message,
                video.State.ToString(),
                video.ReasonCode,
                video.Message,
                videoDiagnostics.Width,
                videoDiagnostics.Height,
                videoDiagnostics.SubmittedFrames,
                videoDiagnostics.DroppedFrames,
                videoDiagnostics.FramesPerSecond,
                videoDiagnostics.RequestedResolutionPercent,
                videoDiagnostics.RequestedBitrateMbps,
                videoDiagnostics.RequestedMaximumFramesPerSecond,
                videoDiagnostics.AverageBitrateMbps,
                videoDiagnostics.PeakBitrateMbps,
                videoDiagnostics.AverageLatencyMilliseconds,
                audio.State.ToString(),
                audio.ReasonCode,
                audio.Message,
                audioDiagnostics.DecodedPackets,
                audioDiagnostics.DroppedBuffers,
                audioDiagnostics.BufferedDuration.TotalMilliseconds,
                !string.IsNullOrWhiteSpace(audioDiagnostics.OutputDeviceId),
                control.State.ToString(),
                control.ReasonCode,
                control.Message,
                controlDiagnostics.SentCommands,
                controlDiagnostics.ReplacedPointerMoves,
                controlDiagnostics.LastQueueDelayMilliseconds,
                playspace.State.ToString(),
                playspace.ReasonCode,
                playspace.Message,
                playspace.Enabled,
                playspace.Multiplier,
                playspace.OffsetX,
                playspace.OffsetY,
                playspace.OffsetZ),
            new DiagnosticBindingSummary(
                bindingPreparation.Prepared,
                bindingPreparation.Revision,
                bindingPreparation.ControllerHand.ToString(),
                bindingPreparation.Profiles
                    .Select(profile => new DiagnosticBindingProfileSummary(
                        profile.ControllerType,
                        profile.Source))
                    .ToArray(),
                videoDiagnostics.BindingState.ToString(),
                videoDiagnostics.BindingReasonCode,
                videoDiagnostics.BindingMessage,
                videoDiagnostics.ControllerType,
                videoDiagnostics.SteamVrInputReady,
                videoDiagnostics.PointerPoseBound,
                videoDiagnostics.PointerPoseActive,
                videoDiagnostics.TouchBound,
                videoDiagnostics.TouchActive,
                videoDiagnostics.GrabBound,
                videoDiagnostics.GrabActive,
                videoDiagnostics.ScaleBound,
                videoDiagnostics.ScaleActive),
            userReport,
            input.LogPath);
    }
}
