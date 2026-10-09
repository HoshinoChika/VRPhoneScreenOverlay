using System.Text.Json;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrBindingProfileStoreTests
{
    [Theory]
    [InlineData((int)OpenVrBindingProfileSource.SavedUser)]
    [InlineData((int)OpenVrBindingProfileSource.CachedUser)]
    [InlineData((int)OpenVrBindingProfileSource.PackagedDefault)]
    public void ThreeTierFallbackPreservesSourceBytesAndSemanticBindings(int source)
    {
        OpenVrBindingProfileSource expectedSource = (OpenVrBindingProfileSource)source;
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        if (expectedSource != OpenVrBindingProfileSource.PackagedDefault) { fixture.WriteCachedBinding("cached-user"); }
        if (expectedSource == OpenVrBindingProfileSource.SavedUser) { fixture.WriteSavedBinding("saved-user"); }
        string packagedPath = Path.Combine(fixture.PackagedDirectory, "bindings", "pico_controller.json");
        byte[] packagedBefore = File.ReadAllBytes(packagedPath);
        byte[]? savedBefore = File.Exists(fixture.SavedBindingPath) ? File.ReadAllBytes(fixture.SavedBindingPath) : null;

        OpenVrPreparedBindingProfile prepared = Assert.Single(fixture.Prepare().Bindings);
        Assert.Equal(expectedSource, prepared.Source);
        string runtimeBefore = File.ReadAllText(prepared.RuntimePath);
        string? cacheBefore = File.Exists(fixture.CachedBindingPath) ? File.ReadAllText(fixture.CachedBindingPath) : null;
        OpenVrDashboardInputGate gate = new();
        gate.Apply(true, new(true, true, true, true), true, out _, out _);
        gate.Apply(false, new(false, false, false, true), true, out _, out _);
        gate.Apply(false, new(false, true, true, true), true, out bool drag, out bool reset);
        Assert.True(drag && reset);
        OpenVrPreparedBindingProfile repeated = Assert.Single(fixture.Prepare().Bindings);

        Assert.Equal(expectedSource, repeated.Source);
        Assert.Equal(runtimeBefore, File.ReadAllText(repeated.RuntimePath));
        Assert.Equal(packagedBefore, File.ReadAllBytes(packagedPath));
        if (savedBefore is not null) { Assert.Equal(savedBefore, File.ReadAllBytes(fixture.SavedBindingPath)); }
        if (cacheBefore is not null) { Assert.Equal(cacheBefore, File.ReadAllText(fixture.CachedBindingPath)); }
        Assert.Contains("/actions/main/in/phoneoverlaygrab", runtimeBefore);
        Assert.Contains("/actions/main/in/lefthandspacedrag", runtimeBefore);
        Assert.Contains("/actions/main/in/righthandspacedrag", runtimeBefore);
        Assert.Contains("/actions/main/in/resetoffsets", runtimeBefore);
        Assert.Equal("/user/hand/left/input/y", ReadSourcePath(repeated.RuntimePath, 2));
    }

    [Theory]
    [InlineData(OpenVrControllerHand.Left, "/user/hand/left/")]
    [InlineData(OpenVrControllerHand.Right, "/user/hand/right/")]
    public void OutsideDismissUsesThePhoneTouchBindingAndPreservesSavedBindings(OpenVrControllerHand hand, string prefix)
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteSavedBinding("user");
        string original = File.ReadAllText(fixture.SavedBindingPath);
        OpenVrPreparedBindingProfile profile = Assert.Single(fixture.Prepare(hand).Bindings);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(profile.RuntimePath));
        JsonElement sources = document.RootElement.GetProperty("bindings").GetProperty("/actions/pointer").GetProperty("sources");
        Assert.NotEmpty(sources.EnumerateArray());
        Assert.All(sources.EnumerateArray(), source =>
        {
            Assert.StartsWith(prefix, source.GetProperty("path").GetString());
        });
        JsonElement dismiss = Assert.Single(sources.EnumerateArray(), source => source.GetRawText().Contains("/actions/pointer/in/menudismiss", StringComparison.Ordinal));
        JsonElement recallSources = document.RootElement.GetProperty("bindings").GetProperty("/actions/phonerecall").GetProperty("sources");
        JsonElement recall = Assert.Single(recallSources.EnumerateArray());
        Assert.Contains("/actions/phonerecall/in/recallmenu", recall.GetRawText());
        Assert.Single(recall.GetProperty("inputs").EnumerateObject());
        Assert.True(recall.GetProperty("inputs").TryGetProperty("long", out _));
        if (recall.TryGetProperty("parameters", out JsonElement parameters))
        {
            Assert.Equal(2, parameters.GetProperty("long_press_delay").GetDouble());
            Assert.False(parameters.TryGetProperty("long_press_expiry", out _));
            Assert.False(parameters.TryGetProperty("double_press_delay", out _));
        }
        Assert.Equal(prefix + "input/trigger", dismiss.GetProperty("path").GetString());
        Assert.Equal(prefix + "input/joystick", recall.GetProperty("path").GetString());
        JsonElement observers = document.RootElement.GetProperty("bindings").GetProperty("/actions/phonebuttonstate").GetProperty("sources");
        Assert.Contains(observers.EnumerateArray(), observer =>
            observer.GetProperty("path").GetString() == recall.GetProperty("path").GetString());
        Assert.True(OpenVrPhoneInteraction.RecallPriority(true, false) > 1);
        Assert.Equal(original, File.ReadAllText(fixture.SavedBindingPath));
    }

    [Fact]
    public void SavedSystemButtonsRemainInUnifiedActionSetAtRuntime()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteSavedBinding("user");

        OpenVrPreparedBindingProfile binding = Assert.Single(fixture.Prepare().Bindings);
        string runtimeJson = File.ReadAllText(binding.RuntimePath);
        string savedJson = File.ReadAllText(fixture.SavedBindingPath);
        using JsonDocument runtimeDocument = JsonDocument.Parse(runtimeJson);
        JsonElement actionSets = runtimeDocument.RootElement.GetProperty("bindings");
        string mainActionSet = actionSets.GetProperty("/actions/main").GetRawText();
        string pointerActionSet = actionSets.GetProperty("/actions/pointer").GetRawText();
        string stateActionSet = actionSets
            .GetProperty("/actions/phonebuttonstate")
            .GetRawText();
        Assert.Contains("/actions/main/in/phoneback", mainActionSet);
        Assert.Contains("/actions/main/in/phonehome", mainActionSet);
        Assert.Contains("/actions/main/in/phonescreenshot", mainActionSet);
        Assert.Contains("/actions/main/in/phoneoverlayscale", mainActionSet);
        Assert.Contains("/actions/main/in/phonepointerpose", mainActionSet);
        Assert.Contains("/actions/pointer/in/phonepointerpose", pointerActionSet);
        Assert.Contains(
            "/actions/phonebuttonstate/in/anyphoneinputpressed",
            stateActionSet);
        Assert.DoesNotContain("\"long\"", stateActionSet);
        Assert.Contains("/input/grip", stateActionSet);
        Assert.False(actionSets.TryGetProperty("/actions/phonebuttons", out _));
        Assert.Contains("/actions/main/in/phoneback", savedJson);
        Assert.DoesNotContain("/actions/phonebuttonstate", savedJson);
    }

    [Fact]
    public void LegacySplitSystemButtonsMigrateIntoUnifiedActionSet()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteLegacySplitSavedBinding("legacy-user");

        OpenVrPreparedBindingProfile binding = Assert.Single(fixture.Prepare().Bindings);
        using JsonDocument runtimeDocument = JsonDocument.Parse(
            File.ReadAllText(binding.RuntimePath));
        JsonElement actionSets = runtimeDocument.RootElement.GetProperty("bindings");
        string mainActionSet = actionSets.GetProperty("/actions/main").GetRawText();
        string pointerActionSet = actionSets.GetProperty("/actions/pointer").GetRawText();
        string stateActionSet = actionSets
            .GetProperty("/actions/phonebuttonstate")
            .GetRawText();

        Assert.Contains("/actions/main/in/phoneback", mainActionSet);
        Assert.Contains("/actions/main/in/phonehome", mainActionSet);
        Assert.Contains("/actions/main/in/phonerecents", mainActionSet);
        Assert.Contains("/actions/main/in/phonecontrolpanel", mainActionSet);
        Assert.Contains("/actions/main/in/phonescreenshot", mainActionSet);
        Assert.Contains("/actions/main/in/phonepointerpose", mainActionSet);
        Assert.Contains("/actions/pointer/in/phonepointerpose", pointerActionSet);
        Assert.Contains(
            "/actions/phonebuttonstate/in/anyphoneinputpressed",
            stateActionSet);
        Assert.False(actionSets.TryGetProperty("/actions/phonebuttons", out _));
    }

    [Fact]
    public void SavedUserBindingBecomesRuntimeDefaultAndUpdatesCache()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteSavedBinding("user");

        OpenVrPreparedInputManifest result = fixture.Prepare();

        OpenVrPreparedBindingProfile binding = Assert.Single(result.Bindings);
        Assert.Equal(OpenVrBindingProfileSource.SavedUser, binding.Source);
        Assert.Equal("user", ReadName(binding.RuntimePath));
        Assert.Equal("user", ReadName(fixture.CachedBindingPath));
        Assert.Equal("bindings/pico_controller.json", ReadRuntimeBindingUrl(result.ActionManifestPath));
    }

    [Fact]
    public void CachedUserBindingSurvivesCorruptSavedFile()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteCachedBinding("cached-user");
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.SavedBindingPath)!);
        File.WriteAllText(fixture.SavedBindingPath, "{broken");

        OpenVrPreparedInputManifest result = fixture.Prepare();

        OpenVrPreparedBindingProfile binding = Assert.Single(result.Bindings);
        Assert.Equal(OpenVrBindingProfileSource.CachedUser, binding.Source);
        Assert.Equal("cached-user", ReadName(binding.RuntimePath));
    }

    [Fact]
    public void CachedUserBindingSurvivesSteamVrRemovingSavedFile()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteSavedBinding("locally-preserved-user");
        OpenVrPreparedBindingProfile first = Assert.Single(fixture.Prepare().Bindings);
        Assert.Equal(OpenVrBindingProfileSource.SavedUser, first.Source);

        File.Delete(fixture.SavedBindingPath);
        OpenVrPreparedBindingProfile recovered = Assert.Single(fixture.Prepare().Bindings);

        Assert.Equal(OpenVrBindingProfileSource.CachedUser, recovered.Source);
        Assert.Equal("locally-preserved-user", ReadName(recovered.RuntimePath));
    }

    [Fact]
    public void PackagedDefaultIsLastFallback()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteSavedBinding("wrong-app", "another.application");

        OpenVrPreparedInputManifest result = fixture.Prepare();

        OpenVrPreparedBindingProfile binding = Assert.Single(result.Bindings);
        Assert.Equal(OpenVrBindingProfileSource.PackagedDefault, binding.Source);
        Assert.Equal("developer", ReadName(binding.RuntimePath));
    }

    [Fact]
    public void SavedBindingRevisionChangesOnlyWhenSavedContentChanges()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteSavedBinding("first");
        string firstRevision = fixture.GetSavedBindingRevision();

        Assert.Equal(firstRevision, fixture.GetSavedBindingRevision());

        fixture.WriteSavedBinding("second");

        Assert.NotEqual(firstRevision, fixture.GetSavedBindingRevision());
    }

    [Fact]
    public void RuntimeBindingMapsPhoneAndLegacyActionsToOppositeHandsWithoutChangingSource()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");

        OpenVrPreparedBindingProfile binding = Assert.Single(
            fixture.Prepare(OpenVrControllerHand.Left).Bindings);

        Assert.Equal("/user/hand/left/pose/tip", ReadFirstPath(binding.RuntimePath));
        Assert.Equal("/user/hand/left/input/trigger", ReadSourcePath(binding.RuntimePath, 0));
        Assert.Equal("/user/hand/right/input/b", ReadSourcePath(binding.RuntimePath, 2));
        Assert.Equal("/user/hand/right/input/a", ReadSourcePath(binding.RuntimePath, 3));
        Assert.Equal("/user/hand/right/input/menu", ReadSourcePath(binding.RuntimePath, 4));
        Assert.Equal(
            "/user/hand/right/pose/tip",
            ReadFirstPath(Path.Combine(fixture.PackagedDirectory, "bindings", "pico_controller.json")));
        Assert.Equal(
            "/user/hand/left/input/y",
            ReadSourcePath(
                Path.Combine(fixture.PackagedDirectory, "bindings", "pico_controller.json"),
                2));
    }

    [Fact]
    public void RightHandRuntimePreservesOriginalTwoHandLayout()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");

        OpenVrPreparedBindingProfile binding = Assert.Single(fixture.Prepare().Bindings);

        Assert.Equal("/user/hand/right/pose/tip", ReadFirstPath(binding.RuntimePath));
        Assert.Equal("/user/hand/right/input/trigger", ReadSourcePath(binding.RuntimePath, 0));
        Assert.Equal("/user/hand/left/input/y", ReadSourcePath(binding.RuntimePath, 2));
    }

    [Fact]
    public void UnknownControllerSavedBySteamVrBecomesRuntimeProfile()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteUnknownSavedBinding("future_controller");

        OpenVrPreparedInputManifest result = fixture.Prepare(OpenVrControllerHand.Left);

        OpenVrPreparedBindingProfile profile = Assert.Single(result.Bindings, profile =>
            profile.ControllerType == "future_controller" &&
            profile.Source == OpenVrBindingProfileSource.SavedUser);
        Assert.Equal("/user/hand/left/pose/tip", ReadFirstPath(profile.RuntimePath));
    }

    [Fact]
    public void MixedPhoneAndPlayspaceSourceFallsBackToLastValidBinding()
    {
        using BindingStoreFixture fixture = new();
        fixture.WritePackagedBinding("developer");
        fixture.WriteCachedBinding("last-valid-user");
        fixture.WriteMixedSavedBinding("invalid-mixed-user");

        OpenVrPreparedBindingProfile profile = Assert.Single(fixture.Prepare().Bindings);

        Assert.Equal(OpenVrBindingProfileSource.CachedUser, profile.Source);
        Assert.Equal("last-valid-user", ReadName(profile.RuntimePath));
    }

    private static string? ReadName(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("name").GetString();
    }

    private static string? ReadRuntimeBindingUrl(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("default_bindings")[0]
            .GetProperty("binding_url")
            .GetString();
    }

    private static string? ReadFirstPath(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("bindings")
            .GetProperty("/actions/main")
            .GetProperty("poses")[0]
            .GetProperty("path")
            .GetString();
    }

    private static string? ReadSourcePath(string path, int index)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("bindings")
            .GetProperty("/actions/main")
            .GetProperty("sources")[index]
            .GetProperty("path")
            .GetString();
    }

    private sealed class BindingStoreFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "VRPhoneScreenOverlay.Tests",
            Guid.NewGuid().ToString("N"));

        public BindingStoreFixture()
        {
            Directory.CreateDirectory(PackagedDirectory);
            File.WriteAllText(
                ActionManifestPath,
                """
                {
                  "default_bindings": [
                    {
                      "controller_type": "pico_controller",
                      "binding_url": "bindings/pico_controller.json"
                    }
                  ],
                  "actions": [],
                  "action_sets": []
                }
                """);
        }

        public string PackagedDirectory => Path.Combine(_root, "package");

        public string ActionManifestPath => Path.Combine(PackagedDirectory, "action_manifest.json");

        public string SavedDirectory => Path.Combine(_root, "documents", "steamvr", "input");

        public string RuntimeDirectory => Path.Combine(_root, "runtime");

        public string SavedBindingPath => Path.Combine(
            SavedDirectory,
            "local.spacedraglite.desktop.v1_pico_controller.json");

        public string CachedBindingPath => Path.Combine(
            RuntimeDirectory,
            "user-bindings",
            "pico_controller.json");

        public OpenVrPreparedInputManifest Prepare(
            OpenVrControllerHand hand = OpenVrControllerHand.Right) => new OpenVrBindingProfileStore(
            ActionManifestPath,
            SavedDirectory,
            RuntimeDirectory,
            hand).Prepare();

        public string GetSavedBindingRevision() => new OpenVrBindingProfileStore(
            ActionManifestPath,
            SavedDirectory,
            RuntimeDirectory).GetSavedBindingRevision();

        public void WritePackagedBinding(string name) => WriteBinding(
            Path.Combine(PackagedDirectory, "bindings", "pico_controller.json"),
            name,
            OpenVrInputManifest.ApplicationKey);

        public void WriteSavedBinding(
            string name,
            string applicationKey = OpenVrInputManifest.ApplicationKey) =>
            WriteBinding(SavedBindingPath, name, applicationKey);

        public void WriteCachedBinding(string name) => WriteBinding(
            CachedBindingPath,
            name,
            OpenVrInputManifest.ApplicationKey);

        public void WriteUnknownSavedBinding(string controllerType) => WriteBinding(
            Path.Combine(SavedDirectory, $"custom-{controllerType}.json"),
            "future-user",
            OpenVrInputManifest.ApplicationKey,
            controllerType);

        public void WriteMixedSavedBinding(string name)
        {
            WriteBinding(SavedBindingPath, name, OpenVrInputManifest.ApplicationKey);
            string json = File.ReadAllText(SavedBindingPath).Replace(
                "\"click\": { \"output\": \"/actions/main/in/phoneoverlaytouch\" }",
                "\"click\": { \"output\": \"/actions/main/in/phoneoverlaytouch\" }, " +
                "\"long\": { \"output\": \"/actions/main/in/lefthandspacedrag\" }");
            File.WriteAllText(SavedBindingPath, json);
        }

        public void WriteLegacySplitSavedBinding(string name)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavedBindingPath)!);
            string json = $$"""
                {
                  "app_key": "{{OpenVrInputManifest.ApplicationKey}}",
                  "controller_type": "pico_controller",
                  "name": "{{name}}",
                  "bindings": {
                    "/actions/main": {
                      "poses": [
                        { "output": "/actions/main/in/phonepointerpose", "path": "/user/hand/right/pose/tip" }
                      ],
                      "sources": [
                        { "path": "/user/hand/right/input/trigger", "inputs": {
                          "click": { "output": "/actions/main/in/phoneoverlaytouch" }
                        } },
                        { "path": "/user/hand/right/input/grip", "inputs": {
                          "click": { "output": "/actions/main/in/phoneoverlaygrab" }
                        } },
                        { "path": "/user/hand/right/input/joystick", "inputs": {
                          "position": { "output": "/actions/main/in/phoneoverlayscale" }
                        } }
                      ]
                    },
                    "/actions/phonebuttons": {
                      "sources": [
                        { "path": "/user/hand/right/input/b", "inputs": {
                          "click": { "output": "/actions/phonebuttons/in/phoneback" },
                          "long": { "output": "/actions/phonebuttons/in/phonehome" }
                        } },
                        { "path": "/user/hand/right/input/a", "inputs": {
                          "click": { "output": "/actions/phonebuttons/in/phonerecents" },
                          "long": { "output": "/actions/phonebuttons/in/phonecontrolpanel" }
                        } },
                        { "path": "/user/hand/right/input/joystick", "inputs": {
                          "click": { "output": "/actions/phonebuttons/in/phonescreenshot" }
                        } }
                      ]
                    },
                    "/actions/phonebuttonstate": { "sources": [] }
                  }
                }
                """;
            File.WriteAllText(SavedBindingPath, json);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static void WriteBinding(string path, string name, string applicationKey)
        {
            WriteBinding(path, name, applicationKey, "pico_controller");
        }

        private static void WriteBinding(
            string path,
            string name,
            string applicationKey,
            string controllerType)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = $$"""
                {
                  "app_key": "{{applicationKey}}",
                  "controller_type": "{{controllerType}}",
                  "name": "{{name}}",
                  "bindings": {
                    "/actions/main": {
                      "poses": [
                        { "output": "/actions/main/in/phonepointerpose", "path": "/user/hand/right/pose/tip" }
                      ],
                      "sources": [
                        {
                          "path": "/user/hand/right/input/trigger",
                          "inputs": { "click": { "output": "/actions/main/in/phoneoverlaytouch" } }
                        },
                        {
                          "path": "/user/hand/right/input/grip",
                          "inputs": { "click": { "output": "/actions/main/in/phoneoverlaygrab" } }
                        },
                        {
                          "path": "/user/hand/left/input/y",
                          "inputs": { "click": { "output": "/actions/main/in/lefthandspacedrag" } }
                        },
                        {
                          "path": "/user/hand/left/input/x",
                          "inputs": { "click": { "output": "/actions/main/in/resetoffsets" } }
                        },
                        {
                          "path": "/user/hand/left/input/menu",
                          "inputs": { "click": { "output": "/actions/main/in/righthandspacedrag" } }
                        },
                        {
                          "path": "/user/hand/right/input/b",
                          "inputs": {
                            "click": { "output": "/actions/main/in/phoneback" },
                            "long": { "output": "/actions/main/in/phonehome" }
                          }
                        },
                        {
                          "path": "/user/hand/right/input/joystick",
                          "inputs": {
                            "click": { "output": "/actions/main/in/phonescreenshot" },
                            "position": { "output": "/actions/main/in/phoneoverlayscale" }
                          }
                        }
                      ]
                    }
                  }
                }
                """;
            File.WriteAllText(path, json);
        }
    }
}
