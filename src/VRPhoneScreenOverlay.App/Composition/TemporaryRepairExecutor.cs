using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Protocols;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.App;

/// <summary>Uses application services only. Parameters apply to the current session, never to the saved settings.</summary>
internal sealed class TemporaryRepairExecutor(IAndroidConnectionService phone, IPhoneOverlayService overlay,
    IPhoneMediaSessionCoordinator media, IAppSettingsService settings, SettingsApplicationService settingsApplication) : ITemporaryRepairExecutor
{
    public ITemporaryRepairAttempt Capture() => new Attempt(phone, overlay, media, settings, settingsApplication,
        phone.Snapshot.SelectedDevice?.DeviceKey, media.Snapshot, settings.Snapshot.Value);

    private sealed class Attempt(IAndroidConnectionService phone, IPhoneOverlayService overlay,
        IPhoneMediaSessionCoordinator media, IAppSettingsService settings, SettingsApplicationService settingsApplication,
        string? deviceKey, PhoneMediaSessionSnapshot capturedMedia, AppSettings capturedSettings) : ITemporaryRepairAttempt
    {
        private int _executed;

        public async Task<TroubleshootingOutcome> ExecuteAsync(TroubleshootingPlan plan, CancellationToken cancellationToken)
        {
            string operation = Guid.NewGuid().ToString("N");
            if (Interlocked.Exchange(ref _executed, 1) != 0) { return Result("skipped", "unknown", "AI_ALREADY_ATTEMPTED"); }
            if (plan.Action == TroubleshootingActions.None) { return Result("skipped", "unknown", "AI_GUIDANCE_ONLY"); }
            if (!TroubleshootingActions.IsSupported(plan.Action) || deviceKey is null ||
                capturedMedia.State is not (PhoneMediaSessionState.Running or PhoneMediaSessionState.Faulted) ||
                !Current(capturedMedia) || !media.CanStart(deviceKey))
            { return Result("skipped", "unknown", "AI_SESSION_CHANGED"); }
            TroubleshootingOutcome result = Result("skipped", "unknown", "AI_SETTINGS_BUSY");
            bool entered = await settingsApplication.TryRunExclusiveAsync(async token =>
            {
                AndroidVideoOptions original = PhoneVideoOptionsFactory.Create(capturedSettings, phone.Snapshot.SelectedDevice, overlay.Snapshot);
                AppSettings temporary = plan.Action == TroubleshootingActions.ReduceVideoLoad ? capturedSettings with
                {
                    VideoResolutionPercent = Math.Min(capturedSettings.VideoResolutionPercent, 75),
                    VideoBitrateMbps = Math.Min(capturedSettings.VideoBitrateMbps, 8),
                    VideoMaximumFramesPerSecond = Math.Min(capturedSettings.VideoMaximumFramesPerSecond, 30),
                } : capturedSettings;
                AndroidVideoOptions options = PhoneVideoOptionsFactory.Create(temporary, phone.Snapshot.SelectedDevice, overlay.Snapshot);
                bool attempted = false;
                PhoneMediaSessionSnapshot? restarted = null;
                try
                {
                    using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(35));
                    attempted = await media.TryRestartScreenAsync(deviceKey, options, () => Current(capturedMedia), deadline.Token, original);
                    if (!attempted) { result = Result("skipped", "unknown", "AI_SESSION_CHANGED"); return; }
                    restarted = media.Snapshot;
                    long before = overlay.Snapshot.SubmittedFrames;
                    for (int index = 0; index < 15; index++)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
                        if (!Current(restarted)) { result = Result("executed", "unknown", "AI_SESSION_CHANGED"); return; }
                        PhoneOverlaySnapshot current = overlay.Snapshot;
                        if (current.State == PhoneOverlayState.Running && current.SubmittedFrames - before >= 5)
                        {
                            result = Result("executed", "unknown", "AI_VIDEO_FRAMES_RESUMED", observations:
                                [new("v1", "video", "new_submitted_frames=" + (current.SubmittedFrames - before).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                                 new("v2", "verification", "Video submission only; touch, audio and visual acceptance require user feedback")]);
                            return;
                        }
                    }
                    result = Result("executed", "not_recovered", "AI_NO_FRAME_RECOVERY");
                }
                catch (OperationCanceledException) { result = Result("cancelled", "unknown", "AI_REPAIR_CANCELLED"); }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or PhoneOverlayServiceException or AndroidConnectionException)
                { result = Result("failed", "unknown", "AI_REPAIR_FAILED"); }
                // No user settings were changed. Restore runtime parameters only while this operation still owns the session.
                if (attempted && temporary != capturedSettings && restarted is not null && Current(restarted))
                {
                    using CancellationTokenSource rollback = new(TimeSpan.FromSeconds(15));
                    try
                    {
                        bool restored = await media.TryRestartScreenAsync(deviceKey, original, () => Current(restarted), rollback.Token);
                        result = result with { Rollback = restored ? "restored" : "failed" };
                    }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException or PhoneOverlayServiceException or AndroidConnectionException)
                    { result = result with { Rollback = "failed" }; }
                }
            }, cancellationToken);
            return entered ? result : Result("skipped", "unknown", "AI_SETTINGS_BUSY");

            TroubleshootingOutcome Result(string execution, string verification, string reason,
                TroubleshootingEvidence[]? observations = null) => new(operation, execution, verification, reason, "not_needed", observations ?? []);
        }

        private bool Current(PhoneMediaSessionSnapshot expected) =>
            phone.Snapshot.IsReady && phone.Snapshot.SelectedDevice?.DeviceKey == deviceKey &&
            ReferenceEquals(media.Snapshot, expected) && settings.Snapshot.Value == capturedSettings;
    }
}
