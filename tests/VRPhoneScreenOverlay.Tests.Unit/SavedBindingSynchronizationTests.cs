using System.Text.Json.Nodes;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class SavedBindingSynchronizationTests
{
    [Fact]
    public async Task SavedEditRegeneratesRuntimeButRollbackPreservesNewUserFile()
    {
        using Files fixture = new();
        byte[] oldCache = await File.ReadAllBytesAsync(fixture.Cache);
        byte[] oldRuntime = await File.ReadAllBytesAsync(fixture.Binding);
        fixture.Save("new screenshot binding", screenshot: true);
        string revision = fixture.Preparation.ReadRevision();
        await using (IBindingDefaultsTransaction transaction = await fixture.Preparation.PrepareAsync(revision, CancellationToken.None))
        {
            Assert.Equal(revision, transaction.SavedRevision);
            Assert.Contains("new screenshot binding", await File.ReadAllTextAsync(transaction.RuntimeBindings["pico_controller"]));
            Assert.Contains("PhoneScreenshot", await File.ReadAllTextAsync(fixture.Binding));
            Assert.DoesNotContain("PhoneControlPanel", await File.ReadAllTextAsync(fixture.Binding));
            Assert.Equal(await File.ReadAllBytesAsync(fixture.Saved), await File.ReadAllBytesAsync(fixture.Cache));
        }
        Assert.Equal(oldCache, await File.ReadAllBytesAsync(fixture.Cache));
        Assert.Equal(oldRuntime, await File.ReadAllBytesAsync(fixture.Binding));
        Assert.Contains("new screenshot binding", await File.ReadAllTextAsync(fixture.Saved));
    }

    [Fact]
    public async Task LaterSaveDoesNotChangeTheSnapshotBeingApplied()
    {
        using Files fixture = new();
        fixture.Save("first edit");
        string first = fixture.Preparation.ReadRevision();
        OpenVrSavedBindingPreparation preparation = fixture.CreatePreparation((_, _) =>
        { fixture.Save("later edit"); return Task.CompletedTask; });
        await using IBindingDefaultsTransaction transaction = await preparation.PrepareAsync(first, CancellationToken.None);
        Assert.Equal(first, transaction.SavedRevision);
        Assert.Contains("first edit", await File.ReadAllTextAsync(fixture.Binding));
        Assert.Contains("later edit", await File.ReadAllTextAsync(fixture.Saved));
        Assert.NotEqual(first, preparation.ReadRevision());
        transaction.Commit();
    }

    [Fact]
    public async Task IncompleteSaveNeverReplacesTheCacheOrRuntime()
    {
        using Files fixture = new();
        byte[] cache = await File.ReadAllBytesAsync(fixture.Cache);
        byte[] runtime = await File.ReadAllBytesAsync(fixture.Binding);
        await File.WriteAllTextAsync(fixture.Saved, "{");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Preparation.PrepareAsync(fixture.Preparation.ReadRevision(), CancellationToken.None).AsTask());
        Assert.Equal(cache, await File.ReadAllBytesAsync(fixture.Cache));
        Assert.Equal(runtime, await File.ReadAllBytesAsync(fixture.Binding));
    }

    [Fact]
    public async Task DebounceCoalescesEditsAndOnlyAcknowledgesTheAppliedRevision()
    {
        FakeSaved saved = new() { Revision = "first" };
        FakeLive live = new();
        await using PhoneBindingSynchronizationService service = Service(saved, live);
        await service.PollAsync(CancellationToken.None);
        saved.Revision = "second";
        await service.PollAsync(CancellationToken.None);
        Assert.Empty(live.Applied);
        live.DuringApply = () => saved.Revision = "third";
        await service.PollAsync(CancellationToken.None);
        Assert.Equal(["second"], live.Applied);
        live.DuringApply = null;
        await service.PollAsync(CancellationToken.None);
        await service.PollAsync(CancellationToken.None);
        Assert.Equal(["second", "third"], live.Applied);
        Assert.Equal(BindingSynchronizationState.Applied, service.Snapshot.State);
    }

    [Fact]
    public async Task FailedActivationRollsBackBothLayersAndCanBeRetriedWithoutChangingSavedBytes()
    {
        FakeSaved saved = new() { Revision = "edit" };
        FakeLive live = new() { Fail = true };
        await using PhoneBindingSynchronizationService service = Service(saved, live);
        await service.PollAsync(CancellationToken.None);
        await service.PollAsync(CancellationToken.None);
        Assert.Equal(BindingSynchronizationState.Failed, service.Snapshot.State);
        Assert.Equal(1, saved.Rollbacks);
        Assert.Equal(1, live.Rollbacks);
        Assert.Empty(live.Applied);
        live.Fail = false;
        service.RequestRetry();
        await service.PollAsync(CancellationToken.None);
        await service.PollAsync(CancellationToken.None);
        Assert.Equal(["edit"], live.Applied);
    }

    [Fact]
    public async Task OpeningAnOverlaySynchronizesThroughTheExistingInputSession()
    {
        FakeSaved saved = new() { Revision = "edit" };
        FakeLive live = new();
        await using PhoneBindingSynchronizationService service = Service(saved, live);
        Assert.True(await service.SynchronizeNowAsync(CancellationToken.None));
        Assert.Equal(["edit"], live.Applied);
    }

    [Fact]
    public async Task DefaultRestorationWaitsForTheSavedEditTransaction()
    {
        FakeSaved saved = new() { Revision = "edit", Delay = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        FakeLive live = new();
        FakeDefaults defaults = new();
        await using PhoneBindingSynchronizationService service = new(saved, live, new(defaults, live), () => true, () => "baseline");
        await service.PollAsync(CancellationToken.None);
        Task edit = service.PollAsync(CancellationToken.None);
        await saved.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task reset = service.RestoreDefaultsAsync(null, null, CancellationToken.None);
        try { Assert.Equal(0, defaults.Calls); }
        finally { saved.Delay.SetResult(); }
        await Task.WhenAll(edit, reset).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, defaults.Calls);
    }

    private static PhoneBindingSynchronizationService Service(FakeSaved saved, FakeLive live) =>
        new(saved, live, new(new FakeDefaults(), live), () => true, () => "baseline");

    private sealed class FakeSaved : ISavedBindingPreparation
    {
        public string Revision { get; set; } = "baseline";
        public TaskCompletionSource? Delay { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Rollbacks { get; private set; }
        public string ReadRevision() => Revision;
        public async ValueTask<IBindingDefaultsTransaction> PrepareAsync(string revision, CancellationToken token)
        {
            Entered.TrySetResult();
            if (Delay is not null) { await Delay.Task.WaitAsync(token); }
            return new FakeFiles(() => Rollbacks++);
        }
    }

    private sealed class FakeDefaults : IBindingDefaultsRestorer
    {
        public int Calls { get; private set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The returned transaction is owned and disposed by the production binding service.")]
        public ValueTask<IBindingDefaultsTransaction> BeginResetAsync(CancellationToken cancellationToken)
        { Calls++; return ValueTask.FromResult<IBindingDefaultsTransaction>(new FakeFiles(() => { })); }
    }

    private sealed class FakeFiles(Action rollback) : IBindingDefaultsTransaction
    {
        public IReadOnlyDictionary<string, string> RuntimeBindings { get; } = new Dictionary<string, string> { ["pico_controller"] = "synthetic" };
        private bool _committed;
        public void Commit() => _committed = true;
        public ValueTask DisposeAsync() { if (!_committed) { rollback(); } return ValueTask.CompletedTask; }
    }

    private sealed class FakeLive : IBindingLiveApplication
    {
        public bool Fail { get; set; }
        public Action? DuringApply { get; set; }
        public List<string> Applied { get; } = [];
        public int Rollbacks { get; private set; }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The returned live transaction is owned and disposed by the production binding service.")]
        public ValueTask<IBindingLiveTransaction> BeginAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IBindingLiveTransaction>(new Application(this));
        private sealed class Application(FakeLive owner) : IBindingLiveTransaction
        {
            private bool _committed;
            public ValueTask ApplyAsync(IReadOnlyDictionary<string, string> runtimeBindings, CancellationToken cancellationToken)
            {
                owner.DuringApply?.Invoke();
                if (owner.Fail) { throw new IOException("synthetic selection failure"); }
                return ValueTask.CompletedTask;
            }
            public void Commit() => _committed = true;
            public void Commit(string revision) { owner.Applied.Add(revision); _committed = true; }
            public ValueTask DisposeAsync() { if (!_committed) { owner.Rollbacks++; } return ValueTask.CompletedTask; }
        }
    }

    private sealed class Files : IDisposable
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "binding-tests", Guid.NewGuid().ToString("N"));
        private readonly string _manifest;
        private readonly string _runtime;
        public string Saved { get; }
        public string Cache => Path.Combine(_runtime, "user-bindings", "pico_controller.json");
        public string Binding => Path.Combine(_runtime, "bindings", "pico_controller.json");
        public OpenVrSavedBindingPreparation Preparation { get; }
        public Files()
        {
            _manifest = OpenVrBuiltInBindings.EnsureSourceFiles(Path.Combine(_root, "package"));
            _runtime = Path.Combine(_root, "runtime");
            Saved = Path.Combine(_root, "saved", OpenVrInputManifest.ApplicationKey + "_pico_controller.json");
            Directory.CreateDirectory(Path.GetDirectoryName(Saved)!);
            File.Copy(Path.Combine(Path.GetDirectoryName(_manifest)!, "bindings", "pico_controller.json"), Saved);
            _ = new OpenVrBindingProfileStore(_manifest, Path.GetDirectoryName(Saved)!, _runtime).Prepare();
            Preparation = CreatePreparation();
        }
        public OpenVrSavedBindingPreparation CreatePreparation(Func<OpenVrControllerHand, CancellationToken, Task>? enrich = null) =>
            new(_manifest, Path.GetDirectoryName(Saved)!, _runtime, () => OpenVrControllerHand.Right, enrich);
        public void Save(string name, bool screenshot = false)
        {
            JsonObject binding = JsonNode.Parse(File.ReadAllText(Saved))!.AsObject();
            binding["name"] = name;
            string json = binding.ToJsonString();
            if (screenshot) { json = json.Replace("PhoneControlPanel", "PhoneScreenshot", StringComparison.OrdinalIgnoreCase); }
            File.WriteAllText(Saved, json);
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
