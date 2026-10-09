using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class BindingDefaultsLifecycleTests
{
    [Fact]
    public async Task RestoreCapturesSelectionAppliesLiveAndCommitsAfterAcceptance()
    {
        List<string> operations = [];
        FakeDefaults defaults = new(operations);
        FakeLive live = new(operations);
        await new PhoneBindingDefaultsService(defaults, live).RestoreAsync(CancellationToken.None);
        Assert.Equal("capture,defaults,apply,live.commit,files.commit,files.dispose,live.dispose", string.Join(',', operations));
        Assert.True(defaults.Transaction.Committed);
        Assert.Equal(defaults.Transaction.RuntimeBindings, live.Transaction.Bindings);
    }

    [Fact]
    public async Task PartialActivationFailureRestoresFilesBeforeTheOldSelection()
    {
        List<string> operations = [];
        FakeDefaults defaults = new(operations);
        FakeLive live = new(operations) { FailApply = true };
        SettingsApplyException error = await Assert.ThrowsAsync<SettingsApplyException>(() =>
            new PhoneBindingDefaultsService(defaults, live).RestoreAsync(CancellationToken.None));
        Assert.Equal("BINDING_DEFAULTS_ROLLED_BACK", error.ReasonCode);
        Assert.False(defaults.Transaction.Committed);
        Assert.Equal("capture,defaults,apply,files.rollback,live.rollback", string.Join(',', operations));
    }

    [Fact]
    public async Task UnavailableSteamVrDoesNotReplaceAnyUserFile()
    {
        List<string> operations = [];
        FakeDefaults defaults = new(operations);
        FakeLive live = new(operations) { FailBegin = true };
        await Assert.ThrowsAsync<SettingsApplyException>(() => new PhoneBindingDefaultsService(defaults, live).RestoreAsync(CancellationToken.None));
        Assert.Equal("capture", string.Join(',', operations));
    }

    [Fact]
    public async Task InvalidDefaultsDoNotApplyOrCommitAnySelection()
    {
        List<string> operations = [];
        FakeDefaults defaults = new(operations) { FailBegin = true };
        FakeLive live = new(operations);
        await Assert.ThrowsAsync<SettingsApplyException>(() => new PhoneBindingDefaultsService(defaults, live).RestoreAsync(CancellationToken.None));
        Assert.Equal("capture,defaults,live.rollback", string.Join(',', operations));
    }

    [Fact]
    public async Task CancellationDuringApplicationStillRollsBackBothLayers()
    {
        List<string> operations = [];
        using CancellationTokenSource stop = new();
        FakeDefaults defaults = new(operations);
        FakeLive live = new(operations) { Cancel = stop };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PhoneBindingDefaultsService(defaults, live).RestoreAsync(stop.Token));
        Assert.Equal("capture,defaults,apply,files.rollback,live.rollback", string.Join(',', operations));
    }

    private sealed class FakeDefaults(List<string> operations) : IBindingDefaultsRestorer
    {
        public bool FailBegin { get; init; }
        public FakeTransaction Transaction { get; } = new(operations);
        public ValueTask<IBindingDefaultsTransaction> BeginResetAsync(CancellationToken cancellationToken)
        {
            operations.Add("defaults");
            if (FailBegin) { throw new InvalidDataException("Defaults are invalid."); }
            return ValueTask.FromResult<IBindingDefaultsTransaction>(Transaction);
        }
    }

    private sealed class FakeTransaction(List<string> operations) : IBindingDefaultsTransaction
    {
        public bool Committed { get; private set; }
        public IReadOnlyDictionary<string, string> RuntimeBindings { get; } = new Dictionary<string, string> { ["pico_controller"] = "prepared" };
        public void Commit() { Committed = true; operations.Add("files.commit"); }
        public ValueTask DisposeAsync() { operations.Add(Committed ? "files.dispose" : "files.rollback"); return ValueTask.CompletedTask; }
    }

    private sealed class FakeLive(List<string> operations) : IBindingLiveApplication
    {
        public bool FailBegin { get; init; }
        public bool FailApply { get; init; }
        public CancellationTokenSource? Cancel { get; init; }
        public FakeApplication Transaction { get; private set; } = null!;
        public ValueTask<IBindingLiveTransaction> BeginAsync(CancellationToken cancellationToken)
        {
            operations.Add("capture");
            if (FailBegin) { throw new IOException("SteamVR is unavailable."); }
            Transaction = new(operations, FailApply, Cancel);
            return ValueTask.FromResult<IBindingLiveTransaction>(Transaction);
        }
    }

    private sealed class FakeApplication(List<string> operations, bool fail, CancellationTokenSource? cancel) : IBindingLiveTransaction
    {
        private bool _committed;
        public IReadOnlyDictionary<string, string>? Bindings { get; private set; }
        public async ValueTask ApplyAsync(IReadOnlyDictionary<string, string> bindings, CancellationToken token)
        {
            operations.Add("apply"); Bindings = bindings;
            if (cancel is not null) { await cancel.CancelAsync(); }
            token.ThrowIfCancellationRequested();
            if (fail) { throw new IOException("SteamVR rejected mapping."); }
        }
        public void Commit() { _committed = true; operations.Add("live.commit"); }
        public ValueTask DisposeAsync() { operations.Add(_committed ? "live.dispose" : "live.rollback"); return ValueTask.CompletedTask; }
    }
}
