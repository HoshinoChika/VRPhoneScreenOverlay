using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Core;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Diagnostics.Usage;
using VRPhoneScreenOverlay.Media;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.Sharing;
using VRPhoneScreenOverlay.SteamVR;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 1 && args[0] == "--translation-capture")
        {
            int range = 65;
            if (args.Length > 3 || (args.Length == 3 && args[2] != "armed") || (args.Length >= 2 &&
                (!int.TryParse(args[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out range) || range is < 30 or > 100)))
            { return 1; }
            return OpenVrTranslationCapture.RunIsolatedCapture(range, args.Length == 3);
        }
        using RuntimeIncidentSession incident = RuntimeIncidentSession.StartAsync().AsTask().GetAwaiter().GetResult();
        using DiagnosticCaptureService capture = new(Path.Combine(AppContext.BaseDirectory, VRPhoneScreenOverlay.Contracts.AppIdentity.MaintenanceExecutableName));
        capture.ObserveIncident(incident);
        try
        {
            _ = capture.StartAutomaticAsync(CancellationToken.None).GetAwaiter().GetResult();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            int result = RunApplication(args, incident, capture);
            if (result == 0) { incident.CompleteAsync().AsTask().GetAwaiter().GetResult(); }
            return result;
        }
        catch (Exception error)
        {
            // Keep the external observer attached while the runtime handles the fatal exception.
            capture.PreserveObserverForFatalExit();
            incident.RecordFailure(error, "application-fatal");
            throw;
        }
    }

    private static int RunApplication(string[] args, RuntimeIncidentSession incident, DiagnosticCaptureService capture)
    {
        int cleanupFailureCount = 0;
        string? updateHealthFile = ReadArgument(args, "--update-health-file");
        string? updateHealthToken = ReadArgument(args, "--update-health-token");

        AppRuntime runtime = new();
        NullSharingSession sharing = new();
        IAndroidConnectionLogSink connectionLog = AndroidConnectionFactory.CreateDefaultLog();
        IAndroidConnectionService phone = AndroidConnectionFactory.CreateDefault(connectionLog);
        using PhoneClipboardStore phoneClipboard = new();
        IAndroidVideoSessionFactory videoSessions =
            AndroidConnectionFactory.CreateDefaultVideoSessions(phone, connectionLog);
        IAndroidControlSessionFactory controlSessions =
            AndroidConnectionFactory.CreateDefaultControlSessions(phone, connectionLog, phoneClipboard);
        IAndroidAudioSessionFactory audioSessions =
            AndroidConnectionFactory.CreateDefaultAudioSessions(phone, connectionLog);
        IAndroidMediaPlaybackControl androidMediaPlayback =
            AndroidConnectionFactory.CreateDefaultMediaPlaybackControl(phone, connectionLog);
        VideoDecoderPreference decoderPreference = args.Contains("--software-decoder", StringComparer.Ordinal)
            ? VideoDecoderPreference.Software : VideoDecoderPreference.Automatic;
        using JsonVideoTrialStore videoTrials = JsonVideoTrialStore.CreateDefault();
        VideoTrialContext VideoContext() => VideoTrialContextFactory.Create(phone.Snapshot.SelectedDevice, decoderPreference);
        IPhoneVideoDecodeProbeService videoProbe =
            new PhoneVideoDecodeProbeService(videoSessions, connectionLog,
                decoderPreference);
        PhoneControlService phoneControl = new(controlSessions, connectionLog);
        PhoneAudioService phoneAudio = new(audioSessions, connectionLog);
        JsonAppSettingsService settings = JsonAppSettingsService.CreateDefault();
        VideoUploadPreferences uploadPreferences = new(settings, Environment.GetEnvironmentVariable("VRPSO_VIDEO_UPLOAD"));
        OpenVrVideoSettingsChannel videoSettings = new();
        OpenVrTranslationChannel translationChannel = new() { PhoneConnected = () => phone.Snapshot.IsReady };
        OpenVrClipboardChannel clipboardChannel = new() { PhoneConnected = () => phone.Snapshot.IsReady };
        using VrMenuOverlayOwner menuOwner = new();
        PhoneOverlayService phoneOverlay = new(
            videoSessions,
            phoneControl,
            AndroidConnectionFactory.CreateDefaultKeyguardStateService(phone),
            connectionLog,
            new PhoneVideoPipelineFactory(videoSettings, decoderPreference, translationChannel, clipboardChannel, menuOwner.AcquireVideo, () => uploadPreferences.Selection), incident, videoTrials, VideoContext, menuOwner);
        OpenVrPlayspaceDragService? playspaceDrag = null;
        PhoneMediaSessionCoordinator mediaSession = new(
            phoneOverlay,
            phoneAudio,
            phoneControl,
            androidMediaPlayback,
            phone,
            () => playspaceDrag?.IsRuntimeReady == true && menuOwner.IsReady, automaticVideoAllowed: options =>
                videoTrials.AutomaticAllowed(VideoContext(), new(options.RequestedResolutionPercent ?? options.ResolutionPercent,
                    options.VideoBitrateBitsPerSecond / 1_000_000, options.MaximumFramesPerSecond)));
        using PhonePlaceholderStateSource placeholderState = new(phone, mediaSession);
        using VrMenuService vrMenu = new(menuOwner,
            () => OpenVrSceneOverlayFactory.Create(videoSettings: videoSettings, translation: translationChannel, clipboard: clipboardChannel,
                phoneState: () => placeholderState.State),
            () => playspaceDrag?.IsRuntimeReady == true, connectionLog, OpenVrMenuRuntime.ReadIdentity,
            async () =>
            {
                await mediaSession.StopForConnectionChangeAsync(CancellationToken.None).ConfigureAwait(false);
                playspaceDrag?.StopService();
                menuOwner.Detach();
                OpenVrMenuRuntime.ResetAfterConsumersStopped();
                playspaceDrag?.Start();
                mediaSession.RequestAutomaticStart();
            }, () => mediaSession.StopAsync(CancellationToken.None).AsTask());
        using PicoMicrophoneKeeper picoMicrophone = new();
        settings.Changed += (_, args) => picoMicrophone.Configure(args.Snapshot.Value.PicoMicrophoneKeeperEnabled);
        ClientServiceConfiguration serviceConfiguration = ClientServiceConfiguration.LoadDefault();
        using HttpUpdateService updateService = new(UpdateServiceOptions.FromConfiguration(serviceConfiguration));
        using DiagnosticHealthMonitor diagnosticHealth = new(incident, () => connectionLog.Health,
            () => DiagnosticContextBuilder.ObserveHardware(phone.Snapshot, phoneOverlay.Snapshot, phoneOverlay.DiagnosticSnapshot));
        using AnonymousDiagnosticsService diagnosticsService = new(
            DiagnosticsServiceOptions.FromConfiguration(serviceConfiguration) with
            {
                RuntimeHealthProvider = diagnosticHealth.Capture,
                CaptureCoverageProvider = capture.ReadCoverage
            });
        EventHandler<OpenVrPlayspaceDragDiagnosticEventArgs> playspaceDiagnosticHandler =
            (_, eventArgs) =>
            {
                OpenVrPlayspaceDragDiagnostic diagnostic = eventArgs.Diagnostic;
                connectionLog.TryWrite(new AndroidConnectionLogEntry(
                    DateTimeOffset.UtcNow,
                    "steamvr_playspace_drag",
                    diagnostic.ReasonCode,
                    diagnostic.Message));
            };
        using RuntimeFlightRecorder flight = new(incident, () =>
        {
            AndroidConnectionSnapshot connection = phone.Snapshot;
            PhoneOverlaySnapshot video = phoneOverlay.DiagnosticSnapshot;
            PhoneAudioSnapshot audio = phoneAudio.Snapshot;
            PhoneControlSnapshot input = phoneControl.Snapshot;
            OpenVrPlayspaceDragSnapshot? space = playspaceDrag?.Snapshot;
            return
            [
                new(FlightComponent.Connection, connection.State.ToString(), connection.ReasonCode, connection.Devices.Count),
                new(FlightComponent.Video, video.State.ToString(), video.ReasonCode, video.SubmittedFrames,
                    video.DroppedFrames, video.AverageLatencyMilliseconds, video.TotalEncodedBytes, video.PipelineStage) { DelaySampleCount = video.LatencySampleCount },
                new(FlightComponent.Audio, audio.State.ToString(), audio.ReasonCode, audio.DecodedPackets,
                    audio.DroppedBuffers, audio.BufferedDuration.TotalMilliseconds),
                new(FlightComponent.Input, input.State.ToString(), input.ReasonCode, input.SentCommands,
                    input.ReplacedPointerMoves, input.LastQueueDelayMilliseconds),
                new(FlightComponent.SteamVr, video.BindingState.ToString(), video.BindingReasonCode),
                new(FlightComponent.Playspace, space?.State.ToString() ?? "Unknown", space?.ReasonCode ?? "UNKNOWN")
            ];
        });
        capture.SetSerializedFlightProvider(() => flight.LatestSerialized);
        using UsageHeartbeatClient heartbeat = UsageHeartbeatClient.CreateDefault(serviceConfiguration);
        PhoneBindingSynchronizationService bindingSynchronization = new(() =>
            playspaceDrag?.IsRuntimeReady == true || phoneOverlay.Snapshot.SteamVrInputReady,
            diagnostic => connectionLog.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow,
                "binding_snapshot_retention", diagnostic.Reason is "completed" or "completed-with-skips" or "rollback-snapshot-removed" or "rollback-reference-retained"
                    ? "BINDING_SNAPSHOT_RETENTION" : "BINDING_SNAPSHOT_CLEANUP_INCOMPLETE",
                "绑定快照引用与清理摘要")
            { BindingSnapshotRetention = diagnostic }));
        IBindingDefaultsTransaction? updateBindings = null;
        SteamVrStartupService startupService = new(() => settings.Snapshot,
            () => playspaceDrag?.IsRuntimeReady == true,
            args.Contains("--steamvr-launch", StringComparer.Ordinal),
            status => connectionLog.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow,
                "steamvr_startup_setting", status.Registration.ReasonCode, status.Registration.Message)
            {
                SteamVrStartup = status.Registration.StartupDiagnostic,
                StartupAttempt = status.Attempts,
                StartupPending = status.Pending,
                ManifestLaunchObserved = status.ManifestLaunchObserved
            }), afterFirstAttempt: () => { playspaceDrag?.Start(); vrMenu.Start(); });
        try
        {
            settings.InitializeAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            videoTrials.InitializeAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            AppSettings initialSettings = settings.Snapshot.Value;
            try
            {
                updateBindings = UpdateBindingReset.BeginAsync(updateHealthFile, updateHealthToken,
                    CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                connectionLog.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow,
                    "update_bindings", "UPDATE_BINDING_DEFAULTS_FAILED", "更新默认绑定写入未完成，本次启动不确认安装健康"));
                throw;
            }
            if (updateBindings is not null)
            {
                connectionLog.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow,
                    "update_bindings", "UPDATE_BINDING_DEFAULTS_WRITTEN", "更新已写入本版本默认绑定，等待启动健康确认"));
            }
            picoMicrophone.Configure(initialSettings.PicoMicrophoneKeeperEnabled);
            OpenVrBindingResult startupBinding = OpenVrBindingRecovery.PrepareLocalFiles();
            connectionLog.TryWrite(new AndroidConnectionLogEntry(
                DateTimeOffset.UtcNow,
                "steamvr_binding_startup",
                startupBinding.ReasonCode,
                startupBinding.Message));
            playspaceDrag = new OpenVrPlayspaceDragService(
                MapControllerHand(initialSettings.ControllerHand),
                1f);
            playspaceDrag.DiagnosticRecorded += playspaceDiagnosticHandler;
            runtime.StartAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            phone.StartAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            SettingsApplicationService settingsApplication = new(settings, phoneOverlay, mediaSession, playspaceDrag, phone, videoTrials, VideoContext);
            using ScreenGuardSettingsController screenGuardSettings = new(settings, phoneControl);
            using VrVideoSettingsController vrVideoSettings = new(videoSettings, settingsApplication, settings, phone);
            using MainForm mainForm = new(
                runtime,
                sharing,
                phone,
                videoProbe,
                phoneOverlay,
                phoneAudio,
                phoneControl,
                mediaSession,
                playspaceDrag,
                settings,
                updateService,
                diagnosticsService,
                connectionLog.CurrentLogPath,
                () => picoMicrophone.State switch
                {
                    PicoMicrophoneState.Disabled => "已关闭",
                    PicoMicrophoneState.WaitingForDevice => "未连接",
                    PicoMicrophoneState.Starting => "连接中",
                    PicoMicrophoneState.Draining => "运行中",
                    _ => "重连中",
                }, settingsApplication, bindingSynchronization, startupBinding,
                ReadArgument(args, "--update-adb-blocked"), startupService, capture, vrMenu, incident.ArmShutdownAsync);
            mainForm.ConfigureVideoUpload(uploadPreferences);
            heartbeat.Start();
            bindingSynchronization.Start();
            mainForm.Shown += (_, _) => startupService.Start();
            var panels = new ApplicationLifetime(incident);
            try
            {
#pragma warning disable CA2000 // Ownership transfers immediately to panels; its finally drains every registered resource, including partial startup.
                string translationDirectory = Path.Combine(AppContext.BaseDirectory, "data", "translation-history");
                TranslationConfigurationStore translationConfiguration = new(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "translation"));
                panels.Register("translation-configuration", () => { translationConfiguration.Dispose(); return ValueTask.CompletedTask; });
                try { translationConfiguration.LoadAsync(CancellationToken.None).GetAwaiter().GetResult(); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException) { }
                DeepSeekTranslationService translationService = new(incident);
                ServiceConnectionProbe serverProbe = new(serviceConfiguration);
                panels.Register("server-connection-probe", () => { serverProbe.Dispose(); return ValueTask.CompletedTask; });
                mainForm.ConfigureServerCheck(serverProbe.CheckAsync);
                panels.Register("translation-service", () => { translationService.Dispose(); return ValueTask.CompletedTask; });
                mainForm.ConfigureTranslation(translationConfiguration, async (key, token) =>
                {
                    var probe = TranslationProbeImage.Create();
                    try
                    {
                        await translationService.ValidateAsync(key, probe, token);
                    }
                    finally { Array.Clear(probe.Bytes); }
                });
                TranslationHistoryStore translationHistory = new(translationDirectory);
                TranslationWorkflow translationWorkflow = new(new OneShotTranslationFrameSource(new ProcessTranslationFrameSource(Environment.ProcessPath!, () => translationConfiguration.Preferences.CaptureRangePercent),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "translation")),
                    new SelectedTranslationClient(() => translationConfiguration.Preferences.ApiSource,
                        translationService.CreateBuiltInClient(serviceConfiguration),
                        translationService.CreateClient(() => translationConfiguration.Key, translationConfiguration.ReportKeyHealth)), translationHistory);
                TranslationShutterSound shutter = new();
                panels.Register("translation-sound", shutter.DisposeAsync);
                TranslationPanelController translationController = new(translationChannel, translationWorkflow,
                    translationHistory, translationConfiguration, shutter.Play, incident, shutter.PlayCountdown);
                panels.Register("translation-panel", translationController.DisposeAsync);
                PhoneScreenshotController screenshotController = new(translationChannel.Screenshot, phone,
                    new ProcessTranslationFrameSource(Environment.ProcessPath!, () => translationConfiguration.Preferences.CaptureRangePercent), new AndroidImageTransfer(phone),
                    () => { if (translationConfiguration.Preferences.ShutterSound) { shutter.Play(); } },
                    () => { if (translationConfiguration.Preferences.ShutterSound) { shutter.PlayCountdown(); } });
                panels.Register("phone-screenshot", screenshotController.DisposeAsync);
                AndroidImageTransfer photoTransfer = new(phone);
                VrcPhotoSyncService photoSync = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "VRChat"),
                    (path, token) => phone.Snapshot.IsReady && phone.Snapshot.SelectedDevice is { } device
                        ? photoTransfer.SendPhotoAsync(device.DeviceKey, path, token)
                        : Task.FromException(new InvalidOperationException("PHOTO_PHONE_UNAVAILABLE")));
                panels.Register("photo-sync", photoSync.DisposeAsync);
                PhotoSyncPreferences photoPreferences = new(settings, photoSync);
                photoPreferences.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();
                mainForm.ConfigurePhotoSync(photoPreferences);
                VideoLinkResolver videoResolver = new();
                panels.Register("video-resolver", () => { videoResolver.Dispose(); return ValueTask.CompletedTask; });
                ClipboardConfigurationStore clipboardConfiguration = new(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "clipboard-settings.json"));
                try { clipboardConfiguration.LoadAsync(CancellationToken.None).GetAwaiter().GetResult(); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
                mainForm.ConfigureClipboard(clipboardConfiguration);
                ClipboardPanelController clipboardController = new(clipboardChannel, phoneClipboard, videoResolver, mainForm.CopyPhoneClipboardAsync, incident, clipboardConfiguration, mainForm.CopyPhoneClipboardGuardedAsync);
                panels.Register("clipboard-panel", clipboardController.DisposeAsync);
                panels.ConfirmWhenShown(mainForm, () =>
                {
                    UpdateHealthReporter.ReportHealthy(
                        updateHealthFile,
                        updateHealthToken,
                        Application.ProductVersion.Split('+', 2)[0]);
                    if (updateBindings is not null)
                    {
                        updateBindings.Commit();
                        updateBindings.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        updateBindings = null;
                        connectionLog.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow,
                            "update_bindings", "UPDATE_BINDING_DEFAULTS_COMMITTED", "更新默认绑定覆盖已完成；实际生效状态由 SteamVR 绑定诊断确认"));
                    }
                });
                Application.Run(mainForm);
                // Ordinary confirmed exit already armed this before preparation.
                // Successful update handoff arms here, before native cleanup.
                incident.ArmShutdownAsync().GetAwaiter().GetResult();
#pragma warning restore CA2000
            }
            finally
            {
                panels.DisposeAsync().AsTask().GetAwaiter().GetResult();
                cleanupFailureCount += panels.Failures.Count;
                mainForm.CompleteMotionSavesAsync().GetAwaiter().GetResult();
            }
        }
        finally
        {
            List<string> cleanupFailures = [];
            cleanupFailures.AddRange(ShutdownSequence.RunAsync(
            [
                new("update-bindings", () => updateBindings?.DisposeAsync() ?? ValueTask.CompletedTask),
                new("heartbeat", heartbeat.DisposeAsync),
                new("steamvr-startup", startupService.DisposeAsync),
                new("binding-synchronization", bindingSynchronization.DisposeAsync),
                new("settings", settings.DisposeAsync),
                new("sharing-stop", () => sharing.StopAsync(CancellationToken.None)),
                new("sharing-dispose", sharing.DisposeAsync),
                new("vr-menu-worker", vrMenu.StopAsync),
                new("media-session", mediaSession.DisposeAsync),
                new("phone-audio", phoneAudio.DisposeAsync),
                new("playspace", () =>
                {
                    if (playspaceDrag is not null)
                    {
                        playspaceDrag.DiagnosticRecorded -= playspaceDiagnosticHandler;
                        playspaceDrag.Dispose();
                    }
                    return ValueTask.CompletedTask;
                }),
                new("phone-overlay", phoneOverlay.DisposeAsync),
                new("vr-menu", vrMenu.DisposeAsync),
                new("phone-control", phoneControl.DisposeAsync),
                new("phone-connection", phone.DisposeAsync),
                new("runtime-stop", () => runtime.StopAsync(CancellationToken.None)),
                new("runtime-dispose", runtime.DisposeAsync),
            ], incident).GetAwaiter().GetResult());
            foreach (string component in cleanupFailures)
            {
                // Failure evidence was already attempted by the bounded shutdown recorder.
                connectionLog.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow,
                    "shutdown", "APP_CLEANUP_FAILED", "组件释放未完成：" + component));
            }
            connectionLog.DisposeAsync().AsTask().GetAwaiter().GetResult();
            cleanupFailureCount += cleanupFailures.Count;
        }

        return cleanupFailureCount == 0 ? 0 : 1;
    }

    private static OpenVrControllerHand MapControllerHand(ControllerHandPreference hand) =>
        hand == ControllerHandPreference.Left
            ? OpenVrControllerHand.Left
            : OpenVrControllerHand.Right;

    private static string? ReadArgument(string[] args, string name)
    {
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
