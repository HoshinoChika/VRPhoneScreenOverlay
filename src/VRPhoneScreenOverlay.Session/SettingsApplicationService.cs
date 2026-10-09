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
    IAndroidConnectionService phone)
{
    private int _applying;

    public async Task<SettingsApplicationResult> ApplyAsync(AppSettings candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
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
        bool videoChanged = candidate.VideoResolutionPercent != previous.VideoResolutionPercent ||
            candidate.VideoBitrateMbps != previous.VideoBitrateMbps ||
            candidate.VideoMaximumFramesPerSecond != previous.VideoMaximumFramesPerSecond;
        bool overlayActive = overlay.Snapshot.State is PhoneOverlayState.Starting or PhoneOverlayState.Running;
        string? deviceKey = phone.Snapshot.SelectedDevice?.DeviceKey;
        try
        {
            await settings.SaveChoicesAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (handChanged)
            {
                await overlay.SwitchControllerHandAsync(Hand(candidate), cancellationToken).ConfigureAwait(false);
                playspace.SetPhoneControllerHand(Hand(candidate));
            }
            if (overlayActive && videoChanged)
            {
                await media.RestartScreenAsync(deviceKey, PhoneVideoOptionsFactory.Create(candidate, phone.Snapshot.SelectedDevice, overlay.Snapshot), null, cancellationToken).ConfigureAwait(false);
            }
            return new(videoChanged, overlayActive && videoChanged);
        }
        catch (Exception exception)
        {
            try
            {
                await settings.SaveChoicesAsync(previous, CancellationToken.None).ConfigureAwait(false);
                if (handChanged)
                {
                    await overlay.SwitchControllerHandAsync(Hand(previous), CancellationToken.None).ConfigureAwait(false);
                    playspace.SetPhoneControllerHand(Hand(previous));
                }
                if (overlayActive && videoChanged)
                {
                    await media.RestartScreenAsync(deviceKey, PhoneVideoOptionsFactory.Create(previous, phone.Snapshot.SelectedDevice, overlay.Snapshot), null, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception rollbackException)
            {
                throw new SettingsApplyException(SettingsReasonCodes.BatchRollbackFailed,
                    "设置应用失败，恢复上一次设置时也遇到错误；请关闭浮窗后重试",
                    new AggregateException(exception, rollbackException));
            }
            throw new SettingsApplyException(SettingsReasonCodes.BatchApplyRolledBack,
                "设置应用失败，已恢复上一次有效设置", exception);
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
