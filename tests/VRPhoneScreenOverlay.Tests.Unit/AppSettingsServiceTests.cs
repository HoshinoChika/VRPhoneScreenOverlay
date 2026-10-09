using System.Text.Json;
using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void SharedVideoApplyWinsOnlyChangedFieldsWhileOtherDesktopDraftsSurvive()
    {
        AppSettings previous = AppSettings.Default;
        AppSettings draft = previous with { VideoBitrateMbps = 32, ControllerHand = ControllerHandPreference.Left };
        AppSettings current = previous with { VideoBitrateMbps = 24, AutoTurnOffPhoneScreen = true };
        AppSettings merged = SettingsDraftMerge.Merge(draft, previous, current);
        Assert.Equal(24, merged.VideoBitrateMbps);
        Assert.True(merged.AutoTurnOffPhoneScreen);
        Assert.Equal(ControllerHandPreference.Left, merged.ControllerHand);
    }

    [Fact]
    public async Task ConcurrentGuardTogglesAndMotionSavePreserveUnrelatedPreferences()
    {
        using TemporarySettingsDirectory directory = new();
        using JsonAppSettingsService settings = directory.CreateService();
        await settings.InitializeAsync(CancellationToken.None);
        MotionPreferences motion = new(5, true, 10, 1.62f, 2, true);
        await Task.WhenAll(settings.ToggleScreenGuardAsync(CancellationToken.None).AsTask(),
            settings.SaveMotionAsync(motion, CancellationToken.None).AsTask());
        Assert.True(settings.Snapshot.Value.AutoTurnOffPhoneScreen);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => settings.ToggleScreenGuardAsync(CancellationToken.None).AsTask()));
        Assert.True(settings.Snapshot.Value.AutoTurnOffPhoneScreen);
        Assert.Equal(motion, MotionPreferences.From(settings.Snapshot.Value));
    }

    [Fact]
    public async Task DisposeWaitsForAcceptedAndQueuedSavesBeforeReleasingTheGate()
    {
        using TemporarySettingsDirectory directory = new();
        using JsonAppSettingsService service = directory.CreateService();
        await service.InitializeAsync(CancellationToken.None);
        using ManualResetEventSlim publishing = new();
        using ManualResetEventSlim resume = new();
        int notifications = 0;
        service.Changed += (_, _) =>
        {
            if (Interlocked.Increment(ref notifications) == 1)
            { publishing.Set(); resume.Wait(TimeSpan.FromSeconds(5)); }
        };
        Task first = Task.Run(async () => await service.SaveNonMotionAsync(
            service.Snapshot.Value with { VideoBitrateMbps = 24 }, CancellationToken.None));
        Assert.True(publishing.Wait(TimeSpan.FromSeconds(5)));
        MotionPreferences motion = new(5, true, 10, 1.62f, 2, true);
        Task queued = service.SaveMotionAsync(motion, CancellationToken.None).AsTask();
        Task dispose = service.DisposeAsync().AsTask();
        try
        {
            Assert.False(dispose.IsCompleted);
            Assert.Same(dispose, service.DisposeAsync().AsTask());
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                service.SaveAsync(AppSettings.Default, CancellationToken.None).AsTask());
        }
        finally { resume.Set(); }
        await Task.WhenAll(first, queued, dispose).WaitAsync(TimeSpan.FromSeconds(5));
        using JsonAppSettingsService reloaded = directory.CreateService();
        await reloaded.InitializeAsync(CancellationToken.None);
        Assert.Equal(24, reloaded.Snapshot.Value.VideoBitrateMbps);
        Assert.Equal(motion, MotionPreferences.From(reloaded.Snapshot.Value));
    }

    [Fact]
    public async Task StaleVideoDraftAndRollbackPreserveNewerMotionWithoutAnyUiSubscriber()
    {
        using TemporarySettingsDirectory directory = new();
        using JsonAppSettingsService service = directory.CreateService();
        await service.InitializeAsync(CancellationToken.None);
        AppSettings before = service.Snapshot.Value;
        MotionPreferences motion = new(5, true, 10, 1.62f, 2, true);
        await service.SaveMotionAsync(motion, CancellationToken.None);
        await service.SaveNonMotionAsync(before with { VideoBitrateMbps = 24 }, CancellationToken.None);
        Assert.Equal(motion, MotionPreferences.From(service.Snapshot.Value));
        Assert.Equal(24, service.Snapshot.Value.VideoBitrateMbps);
        await service.SaveNonMotionAsync(before, CancellationToken.None);
        Assert.Equal(motion, MotionPreferences.From(service.Snapshot.Value));
        Assert.Equal(before.VideoBitrateMbps, service.Snapshot.Value.VideoBitrateMbps);
        using JsonAppSettingsService reloaded = directory.CreateService();
        await reloaded.InitializeAsync(CancellationToken.None);
        Assert.Equal(service.Snapshot.Value, reloaded.Snapshot.Value);
    }

    [Fact]
    public async Task MotionParametersRoundTripWithoutPersistingRuntimeMotion()
    {
        using TemporarySettingsDirectory directory = new();
        using JsonAppSettingsService service = directory.CreateService();
        await service.InitializeAsync(CancellationToken.None);
        AppSettings chosen = service.Snapshot.Value with
        {
            PlayspaceFlingStrength = 3.25f,
            PlayspaceGravity = 1.62f,
            PlayspaceFriction = 12.5f,
            PlayspaceResetAllOffsets = true,
            PicoMicrophoneKeeperEnabled = true,
        };
        await service.SaveAsync(chosen, CancellationToken.None);
        using JsonAppSettingsService reloaded = directory.CreateService();
        await reloaded.InitializeAsync(CancellationToken.None);
        Assert.Equal(chosen, reloaded.Snapshot.Value);
        string json = await File.ReadAllTextAsync(directory.SettingsPath);
        Assert.DoesNotContain("velocity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flingEnabled", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SchemaFiveKeepsPhoneSettingsAndAddsSafeMotionDefaults()
    {
        using TemporarySettingsDirectory directory = new();
        await File.WriteAllTextAsync(directory.SettingsPath,
            """{"schemaVersion":5,"controllerHand":"Left","updateChannel":"Beta","videoResolutionPercent":80,"videoBitrateMbps":24,"videoMaximumFramesPerSecond":45,"autoTurnOffPhoneScreen":true}""");
        using JsonAppSettingsService service = directory.CreateService();
        await service.InitializeAsync(CancellationToken.None);
        AppSettings value = service.Snapshot.Value;
        Assert.Equal(ControllerHandPreference.Left, value.ControllerHand);
        Assert.Equal(80, value.VideoResolutionPercent);
        Assert.Equal(24, value.VideoBitrateMbps);
        Assert.True(value.AutoTurnOffPhoneScreen);
        Assert.Equal(1, value.PlayspaceFlingStrength);
        Assert.Equal(9.8f, value.PlayspaceGravity);
        Assert.Equal(0, value.PlayspaceFriction);
        Assert.False(value.PlayspaceResetAllOffsets);
        Assert.False(value.PicoMicrophoneKeeperEnabled);
    }

    [Theory]
    [InlineData(null, null, ControllerHandPreference.Right)]
    [InlineData("Other", "Beta", ControllerHandPreference.Right)]
    [InlineData("99", "Beta", ControllerHandPreference.Right)]
    [InlineData("Left", "Preview", ControllerHandPreference.Left)]
    [InlineData("Left", "99", ControllerHandPreference.Left)]
    public async Task InvalidEnumFieldsAreRepairedWithoutDiscardingValidSettings(
        string? hand,
        string? channel,
        ControllerHandPreference expectedHand)
    {
        using TemporarySettingsDirectory directory = new();
        await File.WriteAllTextAsync(directory.SettingsPath, JsonSerializer.Serialize(new
        {
            schemaVersion = AppSettings.CurrentSchemaVersion,
            controllerHand = hand,
            updateChannel = channel,
            videoResolutionPercent = 80,
            videoBitrateMbps = 24,
            videoMaximumFramesPerSecond = 45,
            keepAwakeWhileGrabbed = true,
            vrUnlockKeypadEnabled = true,
        }));
        using JsonAppSettingsService service = directory.CreateService();

        await service.InitializeAsync(CancellationToken.None);

        AppSettings value = service.Snapshot.Value;
        Assert.Equal(expectedHand, value.ControllerHand);
        Assert.Equal(UpdateChannel.Beta, value.UpdateChannel);
        Assert.Equal(80, value.VideoResolutionPercent);
        Assert.Equal(24, value.VideoBitrateMbps);
        Assert.Equal(45, value.VideoMaximumFramesPerSecond);
        Assert.True(value.KeepAwakeWhileGrabbed);
        Assert.True(value.VrUnlockKeypadEnabled);
        Assert.Equal(SettingsReasonCodes.ValuesRepaired, service.Snapshot.ReasonCode);
        Assert.True(service.Snapshot.IsPersisted);
        using JsonAppSettingsService reloaded = directory.CreateService();
        await reloaded.InitializeAsync(CancellationToken.None);
        Assert.Equal(value, reloaded.Snapshot.Value);
        Assert.Equal(SettingsReasonCodes.Loaded, reloaded.Snapshot.ReasonCode);
    }

    [Fact]
    public async Task MissingEnumFieldsDoNotPreventStartup()
    {
        using TemporarySettingsDirectory directory = new();
        await File.WriteAllTextAsync(directory.SettingsPath, "{}");
        using JsonAppSettingsService service = directory.CreateService();

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal(AppSettings.Default, service.Snapshot.Value);
        Assert.True(service.Snapshot.IsPersisted);
    }

    [Fact]
    public async Task MigratesExistingControllerHandWithoutDeletingLegacyFile()
    {
        using TemporarySettingsDirectory directory = new();
        await File.WriteAllTextAsync(
            directory.LegacyPath,
            """
            {
              "controllerHand": "Left"
            }
            """);
        using JsonAppSettingsService service = directory.CreateService();

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal(ControllerHandPreference.Left, service.Snapshot.Value.ControllerHand);
        Assert.Equal("SETTINGS_INPUT_MIGRATED", service.Snapshot.ReasonCode);
        Assert.True(File.Exists(directory.LegacyPath));
        Assert.True(File.Exists(directory.SettingsPath));
        using JsonDocument saved = JsonDocument.Parse(
            await File.ReadAllTextAsync(directory.SettingsPath));
        Assert.Equal(
            "Left",
            saved.RootElement.GetProperty("controllerHand").GetString());
    }

    [Fact]
    public async Task CorruptSettingsUseAndPersistSafeDefaults()
    {
        using TemporarySettingsDirectory directory = new();
        await File.WriteAllTextAsync(directory.SettingsPath, "{broken");
        using JsonAppSettingsService service = directory.CreateService();

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal(AppSettings.Default, service.Snapshot.Value);
        Assert.Equal("SETTINGS_CORRUPT_DEFAULTED", service.Snapshot.ReasonCode);
        _ = JsonDocument.Parse(await File.ReadAllTextAsync(directory.SettingsPath));
    }

    [Fact]
    public async Task InvalidValuesAreRepairedIndividually()
    {
        using TemporarySettingsDirectory directory = new();
        await File.WriteAllTextAsync(
            directory.SettingsPath,
            """
            {
              "schemaVersion": 99,
              "controllerHand": "Left",
              "videoResolutionPercent": 1000,
              "videoBitrateMbps": 31,
              "videoMaximumFramesPerSecond": 59,
              "updateChannel": "Beta"
            }
            """);
        using JsonAppSettingsService service = directory.CreateService();

        await service.InitializeAsync(CancellationToken.None);

        Assert.Equal(AppSettings.CurrentSchemaVersion, service.Snapshot.Value.SchemaVersion);
        Assert.Equal(ControllerHandPreference.Left, service.Snapshot.Value.ControllerHand);
        Assert.Equal(100, service.Snapshot.Value.VideoResolutionPercent);
        Assert.Equal(16, service.Snapshot.Value.VideoBitrateMbps);
        Assert.Equal(60, service.Snapshot.Value.VideoMaximumFramesPerSecond);
        Assert.Equal(UpdateChannel.Beta, service.Snapshot.Value.UpdateChannel);
        Assert.False(service.Snapshot.Value.KeepAwakeWhileGrabbed);
        Assert.False(service.Snapshot.Value.VrUnlockKeypadEnabled);
        Assert.Equal("SETTINGS_VALUES_REPAIRED", service.Snapshot.ReasonCode);
    }

    [Fact]
    public async Task SavePublishesChangeAndLeavesNoTemporaryFile()
    {
        using TemporarySettingsDirectory directory = new();
        using JsonAppSettingsService service = directory.CreateService();
        await service.InitializeAsync(CancellationToken.None);
        AppSettingsChangedEventArgs? observed = null;
        service.Changed += (_, eventArgs) => observed = eventArgs;
        AppSettings changed = AppSettings.Default with
        {
            VideoResolutionPercent = 80,
            VideoBitrateMbps = 24,
            VideoMaximumFramesPerSecond = 45,
            VrUnlockKeypadEnabled = true,
            AutoOpenPhoneOverlay = true,
            AutoTurnOffPhoneScreen = true,
            LaunchWithSteamVr = true,
            MinimizeAfterOverlayOpened = true,
        };

        await service.SaveAsync(changed, CancellationToken.None);

        Assert.Equal(changed, service.Snapshot.Value);
        Assert.Equal(changed, observed?.Snapshot.Value);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
        using JsonDocument saved = JsonDocument.Parse(
            await File.ReadAllTextAsync(directory.SettingsPath));
        Assert.False(saved.RootElement.GetProperty("keepAwakeWhileGrabbed").GetBoolean());
        Assert.True(saved.RootElement.GetProperty("vrUnlockKeypadEnabled").GetBoolean());
        using JsonAppSettingsService reloaded = directory.CreateService();
        await reloaded.InitializeAsync(CancellationToken.None);
        Assert.Equal(changed, reloaded.Snapshot.Value);
    }

    [Fact]
    public async Task EveryToggleCombinationAndHandSurvivesSaveAndFreshLoad()
    {
        using TemporarySettingsDirectory directory = new();
        using JsonAppSettingsService service = directory.CreateService();
        await service.InitializeAsync(CancellationToken.None);
        // Visit all six switches together, then turn everything off again. This
        // exercises replacement of an existing file, including true -> false.
        for (int index = 0; index <= 64; index++)
        {
            int flags = index % 64;
            AppSettings expected = AppSettings.Default with
            {
                ControllerHand = (flags & 1) != 0 ? ControllerHandPreference.Left : ControllerHandPreference.Right,
                VideoResolutionPercent = 75,
                VideoBitrateMbps = 32,
                VideoMaximumFramesPerSecond = 30,
                KeepAwakeWhileGrabbed = (flags & 1) != 0,
                VrUnlockKeypadEnabled = (flags & 2) != 0,
                AutoOpenPhoneOverlay = (flags & 4) != 0,
                AutoTurnOffPhoneScreen = (flags & 8) != 0,
                LaunchWithSteamVr = (flags & 16) != 0,
                MinimizeAfterOverlayOpened = (flags & 32) != 0,
            };
            await service.SaveAsync(expected, CancellationToken.None);
            using JsonAppSettingsService restarted = directory.CreateService();
            await restarted.InitializeAsync(CancellationToken.None);
            Assert.Equal(expected, restarted.Snapshot.Value);
            Assert.Equal(SettingsReasonCodes.Loaded, restarted.Snapshot.ReasonCode);
            Assert.True(restarted.Snapshot.IsPersisted);
        }
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task FailedReplacementPreservesSavedSettingsAndDoesNotPublishSuccess()
    {
        using TemporarySettingsDirectory directory = new();
        using JsonAppSettingsService service = directory.CreateService();
        await service.InitializeAsync(CancellationToken.None);
        AppSettingsSnapshot previous = service.Snapshot;
        byte[] original = await File.ReadAllBytesAsync(directory.SettingsPath);
        int changes = 0;
        service.Changed += (_, _) => changes++;
        // Deny delete sharing on Windows so atomic replacement fails after the
        // temporary file has been written, without relying on administrator ACLs.
        using (FileStream locked = new(directory.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(
                AppSettings.Default with { LaunchWithSteamVr = true }, CancellationToken.None).AsTask());
        }
        Assert.Equal(previous, service.Snapshot);
        Assert.Equal(0, changes);
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.SettingsPath));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
        using JsonAppSettingsService restarted = directory.CreateService();
        await restarted.InitializeAsync(CancellationToken.None);
        Assert.Equal(previous.Value, restarted.Snapshot.Value);
    }

    [Fact]
    public async Task CancelledSavePreservesSavedSettingsAndDoesNotPublishSuccess()
    {
        using TemporarySettingsDirectory directory = new();
        using JsonAppSettingsService service = directory.CreateService();
        await service.InitializeAsync(CancellationToken.None);
        AppSettingsSnapshot previous = service.Snapshot;
        byte[] original = await File.ReadAllBytesAsync(directory.SettingsPath);
        int changes = 0;
        service.Changed += (_, _) => changes++;
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(
            AppSettings.Default with { AutoOpenPhoneOverlay = true }, cancellation.Token).AsTask());
        Assert.Equal(previous, service.Snapshot);
        Assert.Equal(0, changes);
        Assert.Equal(original, await File.ReadAllBytesAsync(directory.SettingsPath));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    private sealed class TemporarySettingsDirectory : IDisposable
    {
        public TemporarySettingsDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vrpso-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string SettingsPath => System.IO.Path.Combine(Path, "settings.json");

        public string LegacyPath => System.IO.Path.Combine(Path, "input.json");

        public JsonAppSettingsService CreateService() => new(SettingsPath, LegacyPath);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
