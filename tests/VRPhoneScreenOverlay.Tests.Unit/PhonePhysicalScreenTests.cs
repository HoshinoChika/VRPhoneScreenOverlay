using System.Collections.Concurrent;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhonePhysicalScreenTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(51, true)]
    public async Task RapidVrTogglesAreCoalescedAndExitFlushesTheFinalPreference(int count, bool expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"guard-coalescing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using JsonAppSettingsService settings = new(Path.Combine(directory, "settings.json"), Path.Combine(directory, "input.json"));
            await settings.InitializeAsync(CancellationToken.None);
            await using FakeSession session = new();
            await using Log log = new();
            await using PhoneControlService control = new(new Factory(session), log);
            await using (ScreenGuardSettingsController controller = new(settings, control))
            {
                for (int index = 0; index < count; index++)
                { control.QueueFromSteamVr(PhoneInputCommandKind.ToggleScreenGuard, 0, 0, 0, 0); }
            }
            Assert.Equal(expected, settings.Snapshot.Value.AutoTurnOffPhoneScreen);
            Assert.Equal(count, control.ScreenGuardRequests);
            Assert.True(control.CoalescedScreenGuardRequests > 0);
            await using JsonAppSettingsService reloaded = new(Path.Combine(directory, "settings.json"), Path.Combine(directory, "input.json"));
            await reloaded.InitializeAsync(CancellationToken.None);
            Assert.Equal(expected, reloaded.Snapshot.Value.AutoTurnOffPhoneScreen);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ScreenGuardKeepsActivityWithoutOrdinaryWakeAndStopsPulsesOnClose()
    {
        await using FakeSession session = new();
        await using Log log = new();
        await using PhoneControlService control = new(new Factory(session), log);
        control.ConfigurePhysicalScreen(true);
        await control.StartAsync("test-device", CancellationToken.None);
        await WaitUntilAsync(() => control.Snapshot.ScreenGuardEnabled);
        control.QueueFromSteamVr(PhoneInputCommandKind.WakeScreen, 0, 0, 0, 0);
        await control.SendAsync(PhoneInputCommandKind.Back, 0, 0, 0, 0, 0, CancellationToken.None);
        Assert.Contains(PhoneInputCommandKind.MaintainDisplayOff, session.Commands);
        Assert.DoesNotContain(PhoneInputCommandKind.WakeScreen, session.Commands);
        Assert.Contains(PhoneInputCommandKind.WakeScreenWhileDisplayOff, session.Commands);
        control.ConfigurePhysicalScreen(false);
        await WaitForAsync(session, PhoneInputCommandKind.WakeScreen);
        await control.StopAsync(CancellationToken.None);
        int count = session.Commands.Count;
        await Task.Delay(2200);
        Assert.Equal(count, session.Commands.Count);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task ExternalSleepCanBeRecoveredFromVrAndReturnsToPhysicalPanelOff()
    {
        await using FakeSession session = new();
        await using Log log = new();
        await using PhoneControlService control = new(new Factory(session), log);
        control.ConfigurePhysicalScreen(true);
        await control.StartAsync("test-device", CancellationToken.None);
        await WaitUntilAsync(() => control.Snapshot.ScreenGuardEnabled);
        session.LogicalAwake = false; // External POWER, not a command from the app.
        control.QueueFromSteamVr(PhoneInputCommandKind.WakeScreen, 0, 0, 0, 0);
        await control.SendAsync(PhoneInputCommandKind.Back, 0, 0, 0, 0, 0, CancellationToken.None);
        Assert.True(session.LogicalAwake);
        Assert.False(session.PanelOn);
        Assert.True(session.LastBackWasUsable);
        Assert.True(control.Snapshot.ScreenGuardEnabled); // User input cannot erase mode state.
        session.LogicalAwake = false;
        session.PanelOn = true;
        await WaitUntilAsync(() => !session.PanelOn);
        Assert.False(session.LogicalAwake); // Maintaining panel-off must not wake Android.
        control.QueueFromSteamVr(PhoneInputCommandKind.WakeScreen, 0, 0, 0, 0);
        await WaitUntilAsync(() => session.LogicalAwake && !session.PanelOn);
        Assert.DoesNotContain(PhoneInputCommandKind.WakeScreen, session.Commands);
    }

    [Fact]
    public async Task SlowGuardHasOnlyOneOutstandingCommandAndDisableWinsAfterItCompletes()
    {
        await using FakeSession session = new() { BlockGuard = true };
        await using Log log = new();
        await using PhoneControlService control = new(new Factory(session), log);
        control.ConfigurePhysicalScreen(true);
        await control.StartAsync("test-device", CancellationToken.None);
        await session.GuardEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(350);
        Assert.Single(session.Commands, kind => kind == PhoneInputCommandKind.MaintainDisplayOff);
        control.ConfigurePhysicalScreen(false);
        session.ReleaseGuard.TrySetResult();
        await WaitForAsync(session, PhoneInputCommandKind.WakeScreen);
        await WaitUntilAsync(() => !control.Snapshot.ScreenGuardEnabled);
        int before = session.Commands.Count;
        await Task.Delay(350);
        Assert.Equal(before, session.Commands.Count);
        Assert.True(session.PanelOn);
    }

    [Fact]
    public async Task SidebarTogglePublishesSharedSettingsAndSurvivesNextConnectionAndReload()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"guard-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using JsonAppSettingsService settings = new(Path.Combine(directory, "settings.json"), Path.Combine(directory, "input.json"));
            await settings.InitializeAsync(CancellationToken.None);
            await settings.SaveAsync(settings.Snapshot.Value with { AutoTurnOffPhoneScreen = true }, CancellationToken.None);
            await using FakeSession session = new();
            await using Log log = new();
            await using PhoneControlService control = new(new Factory(session), log);
            await using ScreenGuardSettingsController controller = new(settings, control);
            int updates = 0;
            settings.Changed += (_, _) => updates++;
            await control.StartAsync("test-device", CancellationToken.None);
            await WaitUntilAsync(() => control.Snapshot.ScreenGuardEnabled);
            control.QueueFromSteamVr(PhoneInputCommandKind.ToggleScreenGuard, 0, 0, 0, 0);
            await WaitUntilAsync(() => !control.Snapshot.ScreenGuardEnabled);
            Assert.False(settings.Snapshot.Value.AutoTurnOffPhoneScreen);
            Assert.True(updates > 0);
            await settings.SaveChoicesAsync(AppSettings.Default with { VideoBitrateMbps = 24, AutoTurnOffPhoneScreen = true }, CancellationToken.None);
            await Task.Delay(250);
            Assert.False(control.Snapshot.ScreenGuardEnabled);
            await control.StopAsync(CancellationToken.None);
            await control.StartAsync("test-device", CancellationToken.None);
            Assert.False(control.Snapshot.ScreenGuardEnabled);
            await using JsonAppSettingsService reloaded = new(Path.Combine(directory, "settings.json"), Path.Combine(directory, "input.json"));
            await reloaded.InitializeAsync(CancellationToken.None);
            Assert.False(reloaded.Snapshot.Value.AutoTurnOffPhoneScreen);
            Assert.Equal(24, reloaded.Snapshot.Value.VideoBitrateMbps);
            control.QueueFromSteamVr(PhoneInputCommandKind.ToggleScreenGuard, 0, 0, 0, 0);
            await WaitUntilAsync(() => settings.Snapshot.Value.AutoTurnOffPhoneScreen && control.Snapshot.ScreenGuardEnabled);
            await settings.SaveNonMotionAsync(settings.Snapshot.Value with { AutoTurnOffPhoneScreen = false }, CancellationToken.None);
            await WaitUntilAsync(() => !control.Snapshot.ScreenGuardEnabled);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task GuardCyclesNeverIssuePanelOnOrWakeEvenAcrossActivityRefreshes()
    {
        await using FakeSession session = new();
        await using Log log = new();
        await using PhoneControlService control = new(new Factory(session), log);
        control.ConfigurePhysicalScreen(true);
        await control.StartAsync("test-device", CancellationToken.None);
        await WaitUntilAsync(() => session.Commands.Count(kind => kind == PhoneInputCommandKind.UserActivity) >= 2);
        Assert.True(session.Commands.Count(kind => kind == PhoneInputCommandKind.MaintainDisplayOff) > 1);
        Assert.DoesNotContain(PhoneInputCommandKind.WakeScreenWhileDisplayOff, session.Commands);
        Assert.DoesNotContain(PhoneInputCommandKind.WakeScreen, session.Commands);
        Assert.DoesNotContain(PhoneInputCommandKind.RestoreDisplayPower, session.Commands);
        Assert.False(session.PanelOn);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!predicate()) { await Task.Delay(20, timeout.Token); }
    }

    private static async Task WaitForAsync(FakeSession session, PhoneInputCommandKind kind)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!session.Commands.Contains(kind)) { await Task.Delay(20, timeout.Token); }
    }

    private sealed class Factory(FakeSession session) : IAndroidControlSessionFactory
    {
        public ValueTask<IAndroidControlSession> OpenAsync(string? deviceKey, CancellationToken cancellationToken) => ValueTask.FromResult<IAndroidControlSession>(session);
    }
    private sealed class FakeSession : IAndroidControlSession
    {
        public string DeviceKey => "test-device";
        public ConcurrentQueue<PhoneInputCommandKind> Commands { get; } = new();
        public bool Disposed { get; private set; }
        public bool BlockGuard { get; init; }
        public TaskCompletionSource GuardEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseGuard { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool LogicalAwake = true;
        public volatile bool PanelOn = true;
        public bool LastBackWasUsable { get; private set; }
        public async ValueTask SendAsync(PhoneInputCommand command, CancellationToken cancellationToken)
        {
            Commands.Enqueue(command.Kind);
            if (command.Kind == PhoneInputCommandKind.MaintainDisplayOff && BlockGuard)
            {
                GuardEntered.TrySetResult();
                await ReleaseGuard.Task.WaitAsync(cancellationToken);
            }
            switch (command.Kind)
            {
                case PhoneInputCommandKind.MaintainDisplayOff: PanelOn = false; break;
                case PhoneInputCommandKind.WakeScreenWhileDisplayOff: LogicalAwake = true; PanelOn = false; break;
                case PhoneInputCommandKind.WakeScreen: LogicalAwake = true; PanelOn = true; break;
                case PhoneInputCommandKind.Back: LastBackWasUsable = LogicalAwake; break;
            }
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Log : IAndroidConnectionLogSink
    {
        public string? CurrentLogPath => null;
        public bool TryWrite(AndroidConnectionLogEntry entry) => true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
