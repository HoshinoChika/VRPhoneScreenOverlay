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
        IPhoneVideoDecodeProbeService videoProbe =
            new PhoneVideoDecodeProbeService(videoSessions, connectionLog);
        PhoneControlService phoneControl = new(controlSessions, connectionLog);
        PhoneAudioService phoneAudio = new(audioSessions, connectionLog);
        OpenVrVideoSettingsChannel videoSettings = new();
        PhoneOverlayService phoneOverlay = new(
            videoSessions,
            phoneControl,
            AndroidConnectionFactory.CreateDefaultKeyguardStateService(phone),
            connectionLog,
            new PhoneVideoPipelineFactory(videoSettings));
        OpenVrPlayspaceDragService? playspaceDrag = null;
        PhoneMediaSessionCoordinator mediaSession = new(
            phoneOverlay,
            phoneAudio,
            phoneControl,
            androidMediaPlayback,
            phone,
            () => playspaceDrag?.IsRuntimeReady == true);
        JsonAppSettingsService settings = JsonAppSettingsService.CreateDefault();
        using PicoMicrophoneKeeper picoMicrophone = new();
        settings.Changed += (_, args) => picoMicrophone.Configure(args.Snapshot.Value.PicoMicrophoneKeeperEnabled);
        ClientServiceConfiguration serviceConfiguration = ClientServiceConfiguration.LoadDefault();
        using HttpUpdateService updateService = new(UpdateServiceOptions.FromConfiguration(serviceConfiguration));
        using AnonymousDiagnosticsService diagnosticsService = new(
            DiagnosticsServiceOptions.FromConfiguration(serviceConfiguration));
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
            playspaceDrag?.IsRuntimeReady == true || phoneOverlay.Snapshot.SteamVrInputReady);
        try
        {
            settings.InitializeAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            AppSettings initialSettings = settings.Snapshot.Value;
            picoMicrophone.Configure(initialSettings.PicoMicrophoneKeeperEnabled);
            OpenVrBindingResult startupRegistration = phoneOverlay.ConfigureSteamVrAutoLaunch(
                initialSettings.LaunchWithSteamVr);
            connectionLog.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow,
                "steamvr_startup_setting", startupRegistration.ReasonCode, startupRegistration.Message));
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
            SettingsApplicationService settingsApplication = new(settings, phoneOverlay, mediaSession, playspaceDrag, phone);
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
                }, settingsApplication, bindingSynchronization, startupRegistration.Succeeded ? startupBinding : startupRegistration);
            UpdateHealthReporter.ReportHealthy(
                updateHealthFile,
                updateHealthToken,
                Application.ProductVersion.Split('+', 2)[0]);
            heartbeat.Start();
            bindingSynchronization.Start();
            Application.Run(mainForm);
            mainForm.CompleteMotionSavesAsync().GetAwaiter().GetResult();
        }
        finally
        {
            List<string> cleanupFailures = [];
            try { heartbeat.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception) { cleanupFailures.Add("heartbeat"); }
            cleanupFailures.AddRange(ShutdownSequence.RunAsync(
            [
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
            ]).GetAwaiter().GetResult());
            foreach (string component in cleanupFailures)
            {
                connectionLog.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow,
                    "shutdown", "APP_CLEANUP_FAILED", "组件释放未完成：" + component));
            }
            connectionLog.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return 0;
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
