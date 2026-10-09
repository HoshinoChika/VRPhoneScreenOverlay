using System.Text.Json;

namespace VRPhoneScreenOverlay.Settings;

public sealed class JsonAppSettingsService : IAppSettingsService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _settingsPath;
    private readonly string _legacyInputPath;
    private readonly object _snapshotGate = new();
    private AppSettingsSnapshot _snapshot = new(
        AppSettings.Default,
        SettingsReasonCodes.NotInitialized,
        "设置服务尚未初始化",
        false);
    private bool _disposed;
    private readonly object _lifetimeGate = new();
    private int _activeOperations;
    private TaskCompletionSource? _idle;
    private Task? _disposeTask;

    public JsonAppSettingsService(string settingsPath, string legacyInputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyInputPath);
        _settingsPath = Path.GetFullPath(settingsPath);
        _legacyInputPath = Path.GetFullPath(legacyInputPath);
    }

    public AppSettingsSnapshot Snapshot
    {
        get
        {
            lock (_snapshotGate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<AppSettingsChangedEventArgs>? Changed;

    public static JsonAppSettingsService CreateDefault()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string directory = Path.Combine(localData, "VRPhoneScreenOverlay");
        return new JsonAppSettingsService(
            Path.Combine(directory, "settings.json"),
            Path.Combine(directory, "input.json"));
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        using OperationLease operation = EnterOperation();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings settings;
            string reasonCode;
            string message;
            bool requiresSave;
            if (!File.Exists(_settingsPath))
            {
                ControllerHandPreference migratedHand = await ReadLegacyHandAsync(cancellationToken)
                    .ConfigureAwait(false);
                settings = AppSettings.Default with { ControllerHand = migratedHand };
                requiresSave = true;
                reasonCode = migratedHand == AppSettings.Default.ControllerHand &&
                    !File.Exists(_legacyInputPath)
                    ? SettingsReasonCodes.DefaultCreated
                    : SettingsReasonCodes.InputMigrated;
                message = reasonCode == SettingsReasonCodes.InputMigrated
                    ? "已迁移原有左右手布局并建立统一设置"
                    : "已建立默认统一设置";
            }
            else
            {
                try
                {
                    string json = await File.ReadAllTextAsync(_settingsPath, cancellationToken)
                        .ConfigureAwait(false);
                    SettingsDocument document = JsonSerializer.Deserialize<SettingsDocument>(
                        json,
                        _jsonOptions) ?? throw new JsonException("Settings document was empty.");
                    AppSettings loaded = document.ToSettings(out bool documentRepaired);
                    settings = AppSettingsPolicy.Normalize(loaded);
                    requiresSave = documentRepaired || settings != loaded;
                    reasonCode = requiresSave ? SettingsReasonCodes.ValuesRepaired : SettingsReasonCodes.Loaded;
                    message = requiresSave
                        ? "部分设置无效，已恢复为安全值"
                        : "统一设置已加载";
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or JsonException or
                        InvalidOperationException or FormatException)
                {
                    settings = AppSettings.Default;
                    requiresSave = true;
                    reasonCode = SettingsReasonCodes.CorruptDefaulted;
                    message = "设置文件损坏，已使用安全默认值";
                }
            }

            bool persisted = true;
            if (requiresSave)
            {
                try
                {
                    await WriteAtomicAsync(settings, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    persisted = false;
                    reasonCode = SettingsReasonCodes.SaveUnavailable;
                    message = "设置目录不可写，本次运行将使用安全设置";
                }
            }

            Publish(new AppSettingsSnapshot(settings, reasonCode, message, persisted));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        using OperationLease operation = EnterOperation();
        ArgumentNullException.ThrowIfNull(settings);
        AppSettings normalized = AppSettingsPolicy.Normalize(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAtomicAsync(normalized, cancellationToken).ConfigureAwait(false);
            Publish(new AppSettingsSnapshot(
                normalized,
                SettingsReasonCodes.Saved,
                "设置已安全保存",
                true));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveNonMotionAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        using OperationLease operation = EnterOperation();
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings value = AppSettingsPolicy.Normalize(MotionPreferences.From(Snapshot.Value).Apply(settings));
            await WriteAtomicAsync(value, cancellationToken).ConfigureAwait(false);
            Publish(new(value, SettingsReasonCodes.Saved, "设置已安全保存", true));
        }
        finally { _gate.Release(); }
    }

    public async ValueTask SaveChoicesAsync(AppSettings choices, CancellationToken cancellationToken)
    {
        using OperationLease operation = EnterOperation();
        ArgumentNullException.ThrowIfNull(choices);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings value = AppSettingsPolicy.Normalize(Snapshot.Value with
            {
                ControllerHand = choices.ControllerHand,
                VideoResolutionPercent = choices.VideoResolutionPercent,
                VideoBitrateMbps = choices.VideoBitrateMbps,
                VideoMaximumFramesPerSecond = choices.VideoMaximumFramesPerSecond,
                UpdateChannel = choices.UpdateChannel,
            });
            await WriteAtomicAsync(value, cancellationToken).ConfigureAwait(false);
            Publish(new(value, SettingsReasonCodes.Saved, "设置已安全保存", true));
        }
        finally { _gate.Release(); }
    }

    public async ValueTask ToggleScreenGuardAsync(CancellationToken cancellationToken)
    {
        using OperationLease operation = EnterOperation();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings value = Snapshot.Value with { AutoTurnOffPhoneScreen = !Snapshot.Value.AutoTurnOffPhoneScreen };
            await WriteAtomicAsync(value, cancellationToken).ConfigureAwait(false);
            Publish(new(value, SettingsReasonCodes.Saved, "设置已安全保存", true));
        }
        finally { _gate.Release(); }
    }

    public async ValueTask SaveMotionAsync(MotionPreferences motion, CancellationToken cancellationToken)
    {
        using OperationLease operation = EnterOperation();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings value = AppSettingsPolicy.Normalize(motion.Apply(Snapshot.Value));
            await WriteAtomicAsync(value, cancellationToken).ConfigureAwait(false);
            Publish(new(value, SettingsReasonCodes.Saved, "设置已安全保存", true));
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<ControllerHandPreference> ReadLegacyHandAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_legacyInputPath))
            {
                return AppSettings.Default.ControllerHand;
            }

            string json = await File.ReadAllTextAsync(_legacyInputPath, cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("controllerHand", out JsonElement hand) &&
                Enum.TryParse(hand.GetString(), true, out ControllerHandPreference migrated) &&
                Enum.IsDefined(migrated))
            {
                return migrated;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
                InvalidOperationException)
        {
        }

        return AppSettings.Default.ControllerHand;
    }

    private async ValueTask WriteAtomicAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("Settings path has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(
                SettingsDocument.FromSettings(settings),
                _jsonOptions);
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_settingsPath))
            {
                File.Replace(temporaryPath, _settingsPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null) { return new ValueTask(_disposeTask); }
            _disposed = true;
            Task idle = _activeOperations == 0 ? Task.CompletedTask
                : (_idle = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            _disposeTask = DisposeWhenIdleAsync(idle);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeWhenIdleAsync(Task idle)
    {
        await idle.ConfigureAwait(false);
        _gate.Dispose();
    }

    private OperationLease EnterOperation()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeOperations++;
            return new OperationLease(this);
        }
    }

    private void LeaveOperation()
    {
        lock (_lifetimeGate)
        {
            _activeOperations--;
            if (_activeOperations == 0) { _idle?.TrySetResult(); }
        }
    }

    private sealed class OperationLease(JsonAppSettingsService owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) { owner.LeaveOperation(); }
        }
    }

    private void Publish(AppSettingsSnapshot snapshot)
    {
        lock (_snapshotGate)
        {
            _snapshot = snapshot;
        }

        Changed?.Invoke(this, new AppSettingsChangedEventArgs(snapshot));
    }

    private sealed record SettingsDocument(
        int SchemaVersion,
        string? ControllerHand,
        int VideoResolutionPercent,
        int VideoBitrateMbps,
        int VideoMaximumFramesPerSecond,
        string? UpdateChannel,
        bool KeepAwakeWhileGrabbed = false,
        bool VrUnlockKeypadEnabled = false,
        bool AutoOpenPhoneOverlay = false,
        bool AutoTurnOffPhoneScreen = false,
        bool LaunchWithSteamVr = false,
        bool MinimizeAfterOverlayOpened = false,
        float PlayspaceFlingStrength = 1,
        float PlayspaceGravity = 9.8f,
        float PlayspaceFriction = 0,
        bool PlayspaceResetAllOffsets = false,
        bool PicoMicrophoneKeeperEnabled = false,
        float PlayspaceMultiplier = 1,
        bool PlayspaceInertiaEnabled = false)
    {
        public AppSettings ToSettings(out bool repaired)
        {
            bool validHand = Enum.TryParse(ControllerHand, true, out ControllerHandPreference hand) &&
                Enum.IsDefined(hand);
            bool validChannel = Enum.TryParse(UpdateChannel, true, out UpdateChannel channel) &&
                Enum.IsDefined(channel);
            repaired = !validHand || !validChannel;
            return new AppSettings(
                SchemaVersion,
                validHand ? hand : AppSettings.Default.ControllerHand,
                VideoResolutionPercent,
                VideoBitrateMbps,
                VideoMaximumFramesPerSecond,
                validChannel ? channel : AppSettings.Default.UpdateChannel,
                KeepAwakeWhileGrabbed,
                VrUnlockKeypadEnabled,
                AutoOpenPhoneOverlay,
                AutoTurnOffPhoneScreen,
                LaunchWithSteamVr,
                MinimizeAfterOverlayOpened,
                PlayspaceFlingStrength, PlayspaceGravity, PlayspaceFriction, PlayspaceResetAllOffsets, PicoMicrophoneKeeperEnabled, PlayspaceMultiplier, PlayspaceInertiaEnabled);
        }

        public static SettingsDocument FromSettings(AppSettings settings) => new(
            settings.SchemaVersion,
            settings.ControllerHand.ToString(),
            settings.VideoResolutionPercent,
            settings.VideoBitrateMbps,
            settings.VideoMaximumFramesPerSecond,
            settings.UpdateChannel.ToString(),
            settings.KeepAwakeWhileGrabbed,
            settings.VrUnlockKeypadEnabled,
            settings.AutoOpenPhoneOverlay,
            settings.AutoTurnOffPhoneScreen,
            settings.LaunchWithSteamVr,
            settings.MinimizeAfterOverlayOpened,
            settings.PlayspaceFlingStrength, settings.PlayspaceGravity,
            settings.PlayspaceFriction, settings.PlayspaceResetAllOffsets, settings.PicoMicrophoneKeeperEnabled, settings.PlayspaceMultiplier, settings.PlayspaceInertiaEnabled);
    }
}
