using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public sealed record SettingsApplicationResult(bool VideoChanged, bool ScreenRestarted);

/// <summary>Settings save, hand switch, screen restart and compensating rollback as one use case.</summary>
public sealed class SettingsApplicationService(
    IAppSettingsService settings,
    IPhoneOverlayService overlay,
    IPhoneMediaSessionCoordinator media,
    IOpenVrPlayspaceDragService playspace,
    IAndroidConnectionService phone, IVideoTrialStore? videoTrials = null, Func<VideoTrialContext>? videoContext = null)
{
    private int _applying;

    public async Task<bool> TryRunExclusiveAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _applying, 1, 0) != 0) { return false; }
        try { await action(cancellationToken).ConfigureAwait(false); return true; }
        finally { Volatile.Write(ref _applying, 0); }
    }
    public VideoTrialSnapshot VideoTrials => videoTrials?.Snapshot ?? VideoTrialSnapshot.Unavailable;
    public bool AutomaticVideoAllowed => videoTrials?.AutomaticAllowed(Context(), VideoParameters.From(settings.Snapshot.Value)) ?? true;
    public bool VideoRecoveryChoiceRequired => VideoTrials.RecoveryRequired ||
        !VideoTrials.AutomaticSuppressed && VideoTrials.StorageStatus == "ready" && !AutomaticVideoAllowed;
    public VideoRecoveryCandidate? VideoRecovery => videoTrials?.FindRecovery(Context());
    public VideoSuccessProfile? CurrentVideoSuccess => videoTrials?.GetSuccess(Context());
    public event EventHandler? VideoRecoveryChanged
    { add { if (videoTrials is not null) { videoTrials.Changed += value; } } remove { if (videoTrials is not null) { videoTrials.Changed -= value; } } }
    private VideoTrialContext Context() => videoContext?.Invoke() ?? VideoTrialContextFactory.Create(phone.Snapshot.SelectedDevice, Media.VideoDecoderPreference.Automatic);

    // Only the explicit user-start path may call this. Automatic reconnect/start
    // keeps AutomaticVideoAllowed as its gate and never acknowledges a failure.
    public async Task PrepareManualVideoStartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _applying, 1, 0) != 0)
        { throw new SettingsApplyException(SettingsReasonCodes.BatchApplyBusy, "设置正在应用，请稍后再试"); }
        try
        {
            if (videoTrials is null || AutomaticVideoAllowed) { return; }
            if (overlay.Snapshot.State is PhoneOverlayState.Starting or PhoneOverlayState.Running or PhoneOverlayState.Stopping)
            { throw new SettingsApplyException("VIDEO_RECOVERY_SCREEN_ACTIVE", "请先关闭浮窗，再重新打开"); }
            // This retains RequireManualStart. Only a successfully persisted Begin
            // for the next explicit start releases automatic-start suppression.
            await videoTrials.AcknowledgeRecoveryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref _applying, 0); }
    }

    public async Task ResolveVideoRecoveryAsync(bool restoreVerified, CancellationToken token)
    {
        if (videoTrials is null) { return; }
        if (Interlocked.CompareExchange(ref _applying, 1, 0) != 0)
        { throw new SettingsApplyException(SettingsReasonCodes.BatchApplyBusy, "设置正在应用，请稍后再试"); }
        try
        {
            if (overlay.Snapshot.State is PhoneOverlayState.Starting or PhoneOverlayState.Running or PhoneOverlayState.Stopping)
            { throw new SettingsApplyException("VIDEO_RECOVERY_SCREEN_ACTIVE", "请先关闭浮窗，再处理恢复提示"); }
            if (restoreVerified)
            {
                VideoRecoveryCandidate profile = VideoRecovery ?? throw new SettingsApplyException("VIDEO_RECOVERY_PROFILE_UNAVAILABLE", "当前设备和解码模式没有可恢复的成功参数");
                await settings.SaveVideoAsync(profile.Parameters, token).ConfigureAwait(false);
            }
            await videoTrials.AcknowledgeRecoveryAsync(token).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref _applying, 0); }
    }

    public async Task<SettingsApplicationResult> ApplyAsync(AppSettings candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        candidate = AppSettingsPolicy.Normalize(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _applying, 1, 0) != 0)
        { throw new SettingsApplyException(SettingsReasonCodes.BatchApplyBusy, "设置正在应用，请稍后再试"); }
        try { return await ApplyOwnedAsync(candidate, cancellationToken).ConfigureAwait(false); }
        finally { Volatile.Write(ref _applying, 0); }
    }

    private async Task<SettingsApplicationResult> ApplyOwnedAsync(AppSettings candidate, CancellationToken cancellationToken)
    {
        AppSettings previous = settings.Snapshot.Value;
        if (candidate == previous) { return new(false, false); }
        bool handChanged = candidate.ControllerHand != previous.ControllerHand;
        bool parametersChanged = candidate.VideoResolutionPercent != previous.VideoResolutionPercent ||
            candidate.VideoBitrateMbps != previous.VideoBitrateMbps ||
            candidate.VideoMaximumFramesPerSecond != previous.VideoMaximumFramesPerSecond;
        bool videoChanged = parametersChanged || candidate.VideoUploadMode != previous.VideoUploadMode;
        bool overlayActive = overlay.Snapshot.State is PhoneOverlayState.Starting or PhoneOverlayState.Running;
        string? deviceKey = phone.Snapshot.SelectedDevice?.DeviceKey;
        string? changeId = null;
        bool handAttempted = false;
        bool restartAttempted = false;
        try
        {
            if (overlayActive && parametersChanged && videoTrials is not null)
            { changeId = await videoTrials.StageChangeAsync(Context(), VideoParameters.From(previous), VideoParameters.From(candidate), cancellationToken).ConfigureAwait(false); }
            await settings.SaveChoicesAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (handChanged)
            {
                handAttempted = true;
                await overlay.SwitchControllerHandAsync(Hand(candidate), cancellationToken).ConfigureAwait(false);
                playspace.SetPhoneControllerHand(Hand(candidate));
            }
            if (overlayActive && videoChanged)
            {
                restartAttempted = true;
                await media.RestartScreenAsync(deviceKey, PhoneVideoOptionsFactory.Create(candidate, phone.Snapshot.SelectedDevice, overlay.Snapshot), null, cancellationToken).ConfigureAwait(false);
            }
            if (changeId is not null) { await videoTrials!.ClearChangeAsync(changeId, cancellationToken).ConfigureAwait(false); }
            return new(videoChanged, overlayActive && videoChanged);
        }
        catch (Exception exception)
        {
            try
            {
                AppSettings restored = await settings.RestoreChoicesAsync(candidate, previous, CancellationToken.None).ConfigureAwait(false);
                if (changeId is not null) { await videoTrials!.ClearChangeAsync(changeId, CancellationToken.None).ConfigureAwait(false); }
                if (handAttempted)
                {
                    await overlay.SwitchControllerHandAsync(Hand(restored), CancellationToken.None).ConfigureAwait(false);
                    playspace.SetPhoneControllerHand(Hand(restored));
                }
                if (restartAttempted)
                {
                    await media.RestartScreenAsync(deviceKey, PhoneVideoOptionsFactory.Create(restored, phone.Snapshot.SelectedDevice, overlay.Snapshot), null, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception rollbackException)
            {
                throw new SettingsApplyException(SettingsReasonCodes.BatchRollbackFailed,
                    "设置应用失败，恢复上一次设置时也遇到错误；请关闭浮窗后重试",
                    new AggregateException(exception, rollbackException));
            }
            throw new SettingsApplyException(SettingsReasonCodes.BatchApplyRolledBack,
                "设置应用失败，已恢复本次更改前的参数", exception);
        }
    }

    private static OpenVrControllerHand Hand(AppSettings value) => value.ControllerHand == ControllerHandPreference.Left
        ? OpenVrControllerHand.Left : OpenVrControllerHand.Right;
}

public sealed class SettingsApplyException(
    string reasonCode,
    string message,
    Exception? inner = null) : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}
