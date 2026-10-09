using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class LiveBindingApplicationTests
{
    [Fact]
    public async Task SavedEditSwitchesOnlyTheChangedControllerAndAcknowledgesItsExactRevision()
    {
        using Fixture fixture = new();
        await fixture.Service.RestoreAsync(CancellationToken.None);
        string other = fixture.Api.Current["knuckles"];
        await using IBindingLiveTransaction application = await fixture.Application.BeginAsync(CancellationToken.None);
        await application.ApplySavedAsync(new Dictionary<string, string>
        { ["pico_controller"] = Path.Combine(fixture.Runtime, "bindings", "pico_controller.json") }, CancellationToken.None);
        application.Commit("saved-edit-revision");
        Assert.Equal(other, fixture.Api.Current["knuckles"]);
        Assert.Equal("saved-edit-revision", fixture.AcceptedRevisions[^1]);
    }

    [Fact]
    public async Task AnUnidentifiedSharedDriverUsesTheSameGenericDefaultsAsItsDisplayedGroup()
    {
        using Fixture fixture = new();
        fixture.Api.Current.Clear();
        fixture.Api.Current["holographic_controller"] = "file:///previous/wmr.json";
        await fixture.Service.RestoreAsync(new(true, "holographic_controller", "test"), "generic", CancellationToken.None);
        JsonObject binding = JsonNode.Parse(await File.ReadAllTextAsync(new Uri(Assert.Single(fixture.Api.Selections).Uri).LocalPath))!.AsObject();
        Assert.Equal("holographic_controller", binding["controller_type"]!.GetValue<string>());
        Assert.Equal("VRPhoneScreen Overlay - Generic Default", binding["name"]!.GetValue<string>());
    }
    [Fact]
    public async Task UnknownRegisteredControllerReceivesGenericDefaultsUnderItsRealType()
    {
        using Fixture fixture = new();
        fixture.Api.Current.Clear();
        fixture.Api.Current["unknown_controller"] = "file:///previous/unknown.json";
        await fixture.Service.RestoreAsync(CancellationToken.None);
        (string type, string uri) = Assert.Single(fixture.Api.Selections);
        Assert.Equal("unknown_controller", type);
        JsonObject binding = JsonNode.Parse(await File.ReadAllTextAsync(new Uri(uri).LocalPath))!.AsObject();
        Assert.Equal(type, binding["controller_type"]!.GetValue<string>());
        Assert.Equal("VRPhoneScreen Overlay - Generic Default", binding["name"]!.GetValue<string>());
        Assert.Equal(1, fixture.Accepted);
    }

    [Fact]
    public async Task UnknownControllerActivationFailureRestoresItsPriorSelectionAndFiles()
    {
        using Fixture fixture = new();
        fixture.Api.Current.Clear();
        fixture.Api.Current["unknown_controller"] = "file:///previous/unknown.json";
        fixture.Api.WrongSelection = true;
        await Assert.ThrowsAsync<SettingsApplyException>(() => fixture.Service.RestoreAsync(CancellationToken.None));
        Assert.Equal("file:///previous/unknown.json", fixture.Api.Current["unknown_controller"]);
        Assert.Empty(Directory.GetFiles(fixture.Saved, "*.json"));
        Assert.Equal(0, fixture.Accepted);
    }
    [Fact]
    public async Task ResetConfirmsTargetsInOneQueryWithoutReloadingEveryBinding()
    {
        using Fixture fixture = new();
        await fixture.Service.RestoreAsync(CancellationToken.None);
        Assert.Equal(1, fixture.Accepted);
        Assert.Equal(2, fixture.Api.Selections.Count);
        Assert.Equal(2, fixture.Api.Requests.Count); // capture + one final confirmation
        Assert.All(fixture.Api.Requests, path => Assert.Equal("/input/getactions.json", path));
        foreach ((string type, string uri) in fixture.Api.Selections)
        {
            Assert.StartsWith("file:", uri);
            Assert.Contains("applied-bindings", uri);
            Assert.Equal(uri, fixture.Api.Current[type]);
            Assert.True(File.Exists(new Uri(uri).LocalPath));
        }
        Assert.Equal(7, Directory.GetFiles(fixture.Saved, "*.json").Length);
    }

    [Fact]
    public async Task AReportedSuccessWithoutSelectingTheTargetRollsBackFilesAndSelections()
    {
        using Fixture fixture = new();
        Directory.CreateDirectory(fixture.Saved);
        string original = OpenVrBuiltInBindings.Bindings["pico_controller"].Replace("/input/grip", "/input/trigger", StringComparison.Ordinal);
        string user = Path.Combine(fixture.Saved, OpenVrInputManifest.ApplicationKey + "_pico_controller.json");
        await File.WriteAllTextAsync(user, original);
        fixture.Api.WrongSelection = true;
        SettingsApplyException failure = await Assert.ThrowsAsync<SettingsApplyException>(() => fixture.Service.RestoreAsync(CancellationToken.None));
        Assert.Equal("BINDING_DEFAULTS_ROLLED_BACK", failure.ReasonCode);
        Assert.Equal(original, await File.ReadAllTextAsync(user));
        Assert.Equal(0, fixture.Accepted);
        Assert.All(fixture.Api.Current.Values, value => Assert.Contains("previous", value));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.Runtime, "applied-bindings")));
    }

    [Fact]
    public async Task AFailureAfterOneControllerAppliesRestoresBothControllerSelections()
    {
        using Fixture fixture = new();
        fixture.Api.FailSecond = true;
        await Assert.ThrowsAsync<SettingsApplyException>(() => fixture.Service.RestoreAsync(CancellationToken.None));
        Assert.Equal(0, fixture.Accepted);
        Assert.All(fixture.Api.Current.Values, value => Assert.Contains("previous", value));
        Assert.Empty(Directory.GetFiles(fixture.Saved, "*.json"));
    }

    [Fact]
    public async Task RepeatedResetRetainsAtMostTheCurrentAndPreviousSnapshots()
    {
        using Fixture fixture = new();
        for (int iteration = 0; iteration < 4; iteration++) { await fixture.Service.RestoreAsync(CancellationToken.None); }
        Assert.Equal(2, Directory.GetDirectories(Path.Combine(fixture.Runtime, "applied-bindings")).Length);
    }

    [Fact]
    public async Task OversizedSteamVrResponsesAreRejectedBeforeReplacingFiles()
    {
        using Fixture fixture = new();
        fixture.Api.Oversized = true;
        await Assert.ThrowsAsync<SettingsApplyException>(() => fixture.Service.RestoreAsync(CancellationToken.None));
        Assert.False(Directory.Exists(fixture.Saved));
        Assert.Empty(fixture.Api.Selections);
    }

    [Fact]
    public async Task SevenRegisteredControllerTypesStillUseOnlyOneFinalQuery()
    {
        using Fixture fixture = new();
        foreach (string type in OpenVrBuiltInBindings.Bindings.Keys) { fixture.Api.Current[type] = "file:///previous/" + type + ".json"; }
        await fixture.Service.RestoreAsync(CancellationToken.None);
        Assert.Equal(7, fixture.Api.Selections.Count);
        Assert.Equal(2, fixture.Api.Requests.Count);
        Assert.Equal(1, fixture.Accepted);
    }

    [Fact]
    public async Task ASelectionThatAppearsOnTheNextStateUpdateIsConfirmedWithoutReapplying()
    {
        using Fixture fixture = new();
        fixture.Api.DelayConfirmation = true;
        await fixture.Service.RestoreAsync(CancellationToken.None);
        Assert.Equal(2, fixture.Api.Selections.Count);
        Assert.Equal(3, fixture.Api.Requests.Count);
        Assert.Equal(1, fixture.Accepted);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly CompiledBindingDefaultsTests.TestDirectory _directory = new();
        public string Saved => Path.Combine(_directory.Path, "saved");
        public string Runtime => Path.Combine(_directory.Path, "runtime");
        public FakeApi Api { get; } = new();
        public int Accepted { get; private set; }
        public PhoneBindingDefaultsService Service { get; }
        public OpenVrLiveBindingApplication Application { get; }
        public List<string> AcceptedRevisions { get; } = [];
        public Fixture()
        {
            Application = new(() => new HttpClient(Api, disposeHandler: false) { BaseAddress = new Uri("http://localhost:27062/") },
                (_, _) => ValueTask.FromResult<IOpenVrBindingSelectionChannel>(new FakeChannel(Api)),
                Path.Combine(Runtime, "applied-bindings"), () => Accepted++, revision => { Accepted++; AcceptedRevisions.Add(revision); });
            Service = new(OpenVrBindingDefaults.CreateBuiltIn(Saved, Runtime, OpenVrControllerHand.Right), Application);
        }
        public void Dispose() { Api.Dispose(); _directory.Dispose(); }
    }

    private sealed class FakeChannel(FakeApi api) : IOpenVrBindingSelectionChannel
    {
        public ValueTask SelectAsync(string type, string uri, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            api.Selections.Add((type, uri));
            api.Current[type] = api.WrongSelection && !uri.Contains("previous", StringComparison.Ordinal) ? "file:///wrong/" + type + ".json" : uri;
            if (api.FailSecond && api.Selections.Count == 2) { throw new IOException("Selection rejected."); }
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeApi : HttpMessageHandler
    {
        public Dictionary<string, string> Current { get; } = new(StringComparer.Ordinal)
        { ["pico_controller"] = "file:///previous/pico.json", ["knuckles"] = "file:///previous/index.json" };
        public List<(string Type, string Uri)> Selections { get; } = [];
        public List<string> Requests { get; } = [];
        public bool WrongSelection { get; set; }
        public bool DelayConfirmation { get; set; }
        public bool FailSecond { get; set; }
        public bool Oversized { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!.AbsolutePath);
            string json;
            if (Oversized) { json = new string(' ', 1_048_577); }
            else if (request.RequestUri!.AbsolutePath == "/input/getactions.json")
            {
                json = new JsonObject
                {
                    ["current_binding_url"] = new JsonObject(Current.Select(item => new KeyValuePair<string, JsonNode?>(item.Key,
                        JsonValue.Create(DelayConfirmation && Requests.Count == 2 ? "file:///previous/" + item.Key + ".json" : item.Value)))),
                    ["default_bindings"] = new JsonObject(),
                }.ToJsonString();
            }
            else
            {
                throw new InvalidOperationException("Reset must not reload or inspect each binding's parameters.");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
