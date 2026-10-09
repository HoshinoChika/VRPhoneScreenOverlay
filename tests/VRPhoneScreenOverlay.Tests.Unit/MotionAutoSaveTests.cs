using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class MotionAutoSaveTests
{
    [Fact]
    public async Task ANewValueDuringAnActiveWriteIsNotLost()
    {
        using Recorder settings = new() { BlockFirst = true };
        MotionSettingsWriter writer = new(settings);
        writer.Request(new(1, false, 1, 9.8f, 0, false));
        await settings.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        for (int i = 1; i <= 20; i++) { writer.Request(new(5, true, i, 20, 50, true)); }
        settings.Release.TrySetResult();
        await writer.FlushAsync();
        Assert.Equal(2, settings.Writes);
        Assert.Equal(20, settings.Snapshot.Value.PlayspaceFlingStrength);
        Assert.False(writer.HasPending);
    }

    [Fact]
    public async Task FlushRetriesOnceAndReportsPersistentWriteFailure()
    {
        using Recorder settings = new() { Fail = true };
        MotionSettingsWriter writer = new(settings);
        writer.Request(new(5, true, 10, 9.8f, 0, false));
        await Assert.ThrowsAsync<IOException>(() => writer.FlushAsync());
        Assert.Equal(2, settings.Writes);
    }

    [Fact]
    public async Task HeldButtonUpdatesCoalesceAndFlushPersistsTheLastValue()
    {
        using Recorder settings = new();
        MotionSettingsWriter writer = new(settings);
        for (int i = 0; i < 100; i++) { writer.Request(new(5, true, i / 10f, 9.8f, 10, false)); }
        await writer.FlushAsync();
        Assert.Equal(1, settings.Writes);
        Assert.Equal(9.9f, settings.Snapshot.Value.PlayspaceFlingStrength);
        Assert.True(settings.Snapshot.Value.PlayspaceInertiaEnabled);
        Assert.True(writer.CoalescedUpdates >= 99);
    }

    [Fact]
    public async Task MotionFieldsSurviveReloadWithoutSavingTheMasterSwitch()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VRPhoneScreenOverlay.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "settings.json");
        try
        {
            using (JsonAppSettingsService settings = new(file, Path.Combine(directory, "input.json")))
            {
                await settings.InitializeAsync(CancellationToken.None);
                await settings.SaveAsync(settings.Snapshot.Value with { VideoBitrateMbps = 24 }, CancellationToken.None);
                await settings.SaveMotionAsync(new(10, true, 5, 20, 50, true), CancellationToken.None);
            }
            using JsonAppSettingsService reload = new(file, Path.Combine(directory, "input.json"));
            await reload.InitializeAsync(CancellationToken.None);
            Assert.Equal(new MotionPreferences(10, true, 5, 20, 50, true), MotionPreferences.From(reload.Snapshot.Value));
            Assert.Equal(24, reload.Snapshot.Value.VideoBitrateMbps);
            string json = await File.ReadAllTextAsync(file);
            Assert.DoesNotContain("playspaceEnabled", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("playspaceDragEnabled", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Recorder : IAppSettingsService
    {
        public AppSettingsSnapshot Snapshot { get; private set; } = new(AppSettings.Default, "", "", true);
        public int Writes { get; private set; }
        public bool BlockFirst { get; init; }
        public bool Fail { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<AppSettingsChangedEventArgs>? Changed;
        public ValueTask InitializeAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public async ValueTask SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Writes++;
            if (Fail) { throw new IOException("Test write failure."); }
            if (BlockFirst && Writes == 1)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            Snapshot = new(settings, "", "", true);
            Changed?.Invoke(this, new(Snapshot));
        }
        public void Dispose() { }
    }
}
