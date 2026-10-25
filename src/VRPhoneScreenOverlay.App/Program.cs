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
        using RuntimeIncidentSession incident = RuntimeIncidentSession.StartAsync().AsTask().GetAwaiter().GetResult();
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            int result = RunApplication(args, incident);
            if (result == 0) { incident.CompleteAsync().AsTask().GetAwaiter().GetResult(); }
            return result;
        }
        catch (Exception error)
        {
            incident.RecordFailure(error, "application-fatal");
            throw;
        }
    }

    private static int RunApplication(string[] args, RuntimeIncidentSession incident)
    {
        int cleanupFailureCount = 0;
        string? updateHealthFile = ReadArgument(args, "--update-health-file");
        string? updateHealthToken = ReadArgument(args, "--update-health-token");

        ApplicationConfiguration.Initialize();

        AppRuntime runtime = new();
        NullSharingSession sharing = new();
        IAndroidConnectionLogSink connectionLog = AndroidConnectionFactory.CreateDefaultLog();
        IAndroidConnectionService phone = AndroidConnectionFactory.CreateDefault(connectionLog);
        IAndroidVideoSessionFactory videoSessions =
            AndroidConnectionFactory.CreateDefaultVideoSessions(phone, connectionLog);
        IAndroidControlSessionFactory controlSessions =
            AndroidConnectionFactory.CreateDefaultControlSessions(phone, connectionLog);
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
        OpenVrVideoSettingsChannel videoSettings = new();
        PhoneOverlayService phoneOverlay = new(
            videoSessions,
            phoneControl,
            AndroidConnectionFactory.CreateDefaultKeyguardStateService(phone),
            connectionLog,
            new PhoneVideoPipelineFactory(videoSettings, decoderPreference), incident, videoTrials, VideoContext);
        OpenVrPlayspaceDragService? playspaceDrag = null;
        PhoneMediaSessionCoordinator mediaSession = new(
            phoneOverlay,
            phoneAudio,
            phoneControl,
            androidMediaPlayback,
            phone,
            () => playspaceDrag?.IsRuntimeReady == true, automaticVideoAllowed: options =>
                videoTrials.AutomaticAllowed(VideoContext(), new(options.RequestedResolutionPercent ?? options.ResolutionPercent,
                    options.VideoBitrateBitsPerSecond / 1_000_000, options.MaximumFramesPerSecond)));
        JsonAppSettingsService settings = JsonAppSettingsService.CreateDefault();
        using PicoMicrophoneKeeper picoMicrophone = new();
        settings.Changed += (_, args) => picoMicrophone.Configure(args.Snapshot.Value.PicoMicrophoneKeeperEnabled);
        ClientServiceConfiguration serviceConfiguration = ClientServiceConfiguration.LoadDefault();
        using HttpUpdateService updateService = new(UpdateServiceOptions.FromConfiguration(serviceConfiguration));
        using DiagnosticHealthMonitor diagnosticHealth = new(incident, () => connectionLog.Health,
            () => DiagnosticContextBuilder.ObserveHardware(phone.Snapshot, phoneOverlay.Snapshot, phoneOverlay.DiagnosticSnapshot));
        TroubleshootingCredential? troubleshootingCredential = TroubleshootingCredential.TryLoadDefaultAsync(CancellationToken.None).GetAwaiter().GetResult();
        using AnonymousDiagnosticsService diagnosticsService = new(
            DiagnosticsServiceOptions.FromConfiguration(serviceConfiguration) with
            {
                RuntimeHealthProvider = diagnosticHealth.Capture,
                TroubleshootingCredential = troubleshootingCredential,
                InvestigationDirectory = troubleshootingCredential is null ? null : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VRPhoneScreenOverlay", "troubleshooting", troubleshootingCredential.StorageId, "investigations")
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
            }));
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
            startupService.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
            OpenVrBindingResult startupRegistration = startupService.Snapshot.Registration;
            OpenVrBindingResult startupBinding = OpenVrBindingRecovery.PrepareLocalBinding();
            connectionLog.TryWrite(new AndroidConnectionLogEntry(
                DateTimeOffset.UtcNow,
                "steamvr_binding_startup",
                startupBinding.ReasonCode,
                startupBinding.Message));
            playspaceDrag = new OpenVrPlayspaceDragService(
                MapControllerHand(initialSettings.ControllerHand),
                1f);
            playspaceDrag.DiagnosticRecorded += playspaceDiagnosticHandler;
            playspaceDrag.Start();
            runtime.StartAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            phone.StartAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            SettingsApplicationService settingsApplication = new(settings, phoneOverlay, mediaSession, playspaceDrag, phone, videoTrials, VideoContext);
            using TroubleshootingClient? troubleshootingClient = troubleshootingCredential is not null && serviceConfiguration.DiagnosticsInitUri is { } diagnosticsUri
                ? new(diagnosticsUri, troubleshootingCredential) : null;
            using TroubleshootingWorkflow? troubleshooting = troubleshootingClient is not null
                ? new(troubleshootingClient, new TemporaryRepairExecutor(phone, phoneOverlay, mediaSession, settings, settingsApplication),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "troubleshooting", troubleshootingCredential!.StorageId, "outbox"),
                    new LocalTroubleshootingInspector(phone, phoneOverlay, phoneAudio, phoneControl, mediaSession,
                        new LocalSoftwareFileInspector(AppContext.BaseDirectory,
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay"),
                            System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Program).Assembly)?.InformationalVersion ?? string.Empty), () => connectionLog.Health,
                        playspaceDrag, settings, new LocalDiagnosticReadTools(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "logs"))))
                : null;
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
                }, settingsApplication, bindingSynchronization, startupRegistration.Succeeded ? startupBinding : startupRegistration,
                ReadArgument(args, "--update-adb-blocked"), startupService, troubleshooting);
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
            heartbeat.Start();
            bindingSynchronization.Start();
            startupService.Start();
            Application.Run(mainForm);
            mainForm.CompleteMotionSavesAsync().GetAwaiter().GetResult();
        }
        finally
        {
            List<string> cleanupFailures = [];
            if (updateBindings is not null)
            {
                try { updateBindings.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch (Exception) { cleanupFailures.Add("update-bindings"); }
            }
            try { heartbeat.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception) { cleanupFailures.Add("heartbeat"); }
            cleanupFailures.AddRange(ShutdownSequence.RunAsync(
            [
                new("steamvr-startup", startupService.DisposeAsync),
                new("binding-synchronization", bindingSynchronization.DisposeAsync),
                new("settings", settings.DisposeAsync),
                new("sharing-stop", () => sharing.StopAsync(CancellationToken.None)),
                new("sharing-dispose", sharing.DisposeAsync),
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
                new("phone-control", phoneControl.DisposeAsync),
                new("phone-connection", phone.DisposeAsync),
                new("runtime-stop", () => runtime.StopAsync(CancellationToken.None)),
                new("runtime-dispose", runtime.DisposeAsync),
            ], incident).GetAwaiter().GetResult());
            foreach (string component in cleanupFailures)
            {
                incident.CheckpointAsync(new("cleanup", "failed", component), CancellationToken.None).AsTask().GetAwaiter().GetResult();
                connectionLog.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow,
                    "shutdown", "APP_CLEANUP_FAILED", "组件释放未完成：" + component));
            }
            connectionLog.DisposeAsync().AsTask().GetAwaiter().GetResult();
            cleanupFailureCount = cleanupFailures.Count;
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
