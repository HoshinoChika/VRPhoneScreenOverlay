using System.Reflection;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class SettingsApplicationServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VideoChangeRestartsOnlyAnActiveScreen(bool active)
    {
        Scenario scenario = new(active, 0);
        SettingsApplicationResult result = await scenario.Service.ApplyAsync(
            AppSettings.Default with { VideoBitrateMbps = 24 }, CancellationToken.None);
        Assert.Equal(active, result.ScreenRestarted);
        Assert.Equal(active ? 1 : 0, scenario.Restarts.Count);
        Assert.Equal(24, scenario.Persisted.VideoBitrateMbps);
        Assert.Equal(1, scenario.Saves);
    }

    [Theory]
    [InlineData(1, SettingsReasonCodes.BatchApplyRolledBack)]
    [InlineData(2, SettingsReasonCodes.BatchRollbackFailed)]
    public async Task RestartFailureRestoresSettingsAndHandWithoutRestartingPhoneAudio(int failures, string expected)
    {
        Scenario scenario = new(true, failures);
        SettingsApplyException error = await Assert.ThrowsAsync<SettingsApplyException>(() => scenario.Service.ApplyAsync(
            AppSettings.Default with { VideoBitrateMbps = 24, ControllerHand = ControllerHandPreference.Left }, CancellationToken.None));
        Assert.Equal(expected, error.ReasonCode);
        Assert.Equal(AppSettings.Default, scenario.Persisted);
        Assert.Equal("Left,Right", string.Join(',', scenario.Hands));
        Assert.Equal("24000000,16000000", string.Join(',', scenario.Restarts));
        Assert.Equal(2, scenario.Saves);
    }

    [Fact]
    public async Task ConcurrentTransactionsAreRejectedWithoutOverwritingTheInFlightSave()
    {
        TaskCompletionSource resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Scenario scenario = new(true, 0) { BlockFirstRestart = resumed.Task };
        Task<SettingsApplicationResult> first = scenario.Service.ApplyAsync(AppSettings.Default with { VideoBitrateMbps = 24 }, CancellationToken.None);
        try
        {
            SettingsApplyException error = await Assert.ThrowsAsync<SettingsApplyException>(() =>
                scenario.Service.ApplyAsync(AppSettings.Default, CancellationToken.None));
            Assert.Equal(SettingsReasonCodes.BatchApplyBusy, error.ReasonCode);
            Assert.Equal(1, scenario.Saves);
        }
        finally { resumed.SetResult(); }
        Assert.True((await first).ScreenRestarted);
    }

    [Fact]
    public async Task UnchangedSettingsHaveNoSideEffects()
    {
        Scenario scenario = new(true, 0);
        await scenario.Service.ApplyAsync(AppSettings.Default, CancellationToken.None);
        Assert.Equal(0, scenario.Saves);
        Assert.Empty(scenario.Restarts);
        Assert.Empty(scenario.Hands);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task VrVideoRequestUsesSharedApplyAndRollbackWithoutChangingOtherSettings(int failures, bool success)
    {
        Scenario scenario = new(true, failures);
        OpenVrVideoSettingsChannel channel = new();
        await using VrVideoSettingsController controller = new(channel, scenario.Service, scenario.Settings, scenario.Phone);
        Assert.True(channel.Request(new(100, 24, 45)));
        Assert.False(channel.Request(new(100, 32, 60)));
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        while (channel.State == OpenVrVideoApplyState.Applying) { await Task.Delay(20, deadline.Token); }
        Assert.Equal(success ? OpenVrVideoApplyState.Applied : OpenVrVideoApplyState.Failed, channel.State);
        Assert.Equal(success ? 24 : 16, scenario.Persisted.VideoBitrateMbps);
        Assert.Equal(AppSettings.Default.ControllerHand, scenario.Persisted.ControllerHand);
        Assert.Equal(success ? 1 : 2, scenario.Restarts.Count);
        Assert.Equal(1, channel.Accepted);
        Assert.Equal(1, channel.Rejected);
    }

    private sealed class Scenario
    {
        public AppSettings Persisted { get; private set; } = AppSettings.Default;
        public int Saves { get; private set; }
        public Task? BlockFirstRestart { get; init; }
        public List<int> Restarts { get; } = [];
        public List<OpenVrControllerHand> Hands { get; } = [];
        public SettingsApplicationService Service { get; }
        public IAppSettingsService Settings { get; }
        public IAndroidConnectionService Phone { get; }

        public Scenario(bool active, int failures)
        {
            IAppSettingsService settings = Make<IAppSettingsService>((method, args) => method.Name switch
            {
                "get_Snapshot" => new AppSettingsSnapshot(Persisted, "test", "test", true),
                "SaveChoicesAsync" => AsProxyReturn(Save((AppSettings)args[0]!)),
                _ => throw new InvalidOperationException("Unexpected settings operation: " + method.Name),
            });
            IPhoneOverlayService overlay = Make<IPhoneOverlayService>((method, args) => method.Name switch
            {
                "get_Snapshot" => new PhoneOverlaySnapshot(active ? PhoneOverlayState.Running : PhoneOverlayState.Stopped, "test", "test"),
                "SwitchControllerHandAsync" => AsProxyReturn(SwitchHand((OpenVrControllerHand)args[0]!)),
                _ => throw new InvalidOperationException("Unexpected overlay operation: " + method.Name),
            });
            IPhoneMediaSessionCoordinator media = Make<IPhoneMediaSessionCoordinator>((method, args) =>
            {
                Assert.Equal("RestartScreenAsync", method.Name);
                Restarts.Add(((AndroidVideoOptions)args[1]!).VideoBitrateBitsPerSecond);
                if (Restarts.Count == 1 && BlockFirstRestart is not null) { return AsProxyReturn(new ValueTask(BlockFirstRestart)); }
                return AsProxyReturn(Restarts.Count <= failures ? ValueTask.FromException(new InvalidOperationException("simulated restart failure")) : ValueTask.CompletedTask);
            });
            IOpenVrPlayspaceDragService playspace = Make<IOpenVrPlayspaceDragService>((method, _) =>
            { Assert.Equal("SetPhoneControllerHand", method.Name); return null; });
            IAndroidConnectionService phone = Make<IAndroidConnectionService>((method, _) =>
            {
                Assert.Equal("get_Snapshot", method.Name);
                return new AndroidConnectionSnapshot(1, AndroidConnectionState.Ready, "test", "test", [], null, DateTimeOffset.UtcNow, null);
            });
            Settings = settings; Phone = phone;
            Service = new(settings, overlay, media, playspace, phone);
        }
        // DispatchProxy unboxes this result for the service, which awaits it exactly once.
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859",
            Justification = "DispatchProxy's Invoke contract requires a boxed object; preserving that boundary also makes the single ValueTask consumer explicit.")]
        private static object AsProxyReturn(ValueTask result) => result;
        private ValueTask Save(AppSettings value) { Persisted = value; Saves++; return ValueTask.CompletedTask; }
        private ValueTask SwitchHand(OpenVrControllerHand hand) { Hands.Add(hand); return ValueTask.CompletedTask; }
        private static T Make<T>(Func<MethodInfo, object?[], object?> call) where T : class
        {
            T proxy = DispatchProxy.Create<T, RecordingProxy>();
            ((RecordingProxy)(object)proxy).Call = call;
            return proxy;
        }
    }

    // DispatchProxy generates a subclass; this type must remain non-sealed.
    public class RecordingProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args ?? []);
    }
}
