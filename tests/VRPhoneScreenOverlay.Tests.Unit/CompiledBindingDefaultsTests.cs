using System.Text.Json.Nodes;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class CompiledBindingDefaultsTests
{
    [Fact]
    public async Task DeletedOrCorruptExportedDefaultsCannotPreventAReset()
    {
        using TestDirectory directory = new();
        string saved = Path.Combine(directory.Path, "saved");
        string runtime = Path.Combine(directory.Path, "runtime");
        OpenVrBindingDefaults store = OpenVrBindingDefaults.CreateBuiltIn(saved, runtime, OpenVrControllerHand.Right);
        await using (IBindingDefaultsTransaction first = await store.BeginResetAsync(CancellationToken.None)) { first.Commit(); }
        string source = Path.Combine(runtime, "default-source");
        File.Delete(Path.Combine(source, "action_manifest.json"));
        foreach (string file in Directory.EnumerateFiles(Path.Combine(source, "bindings"))) { await File.WriteAllTextAsync(file, "{}"); }
        await using IBindingDefaultsTransaction next = await store.BeginResetAsync(CancellationToken.None);
        Assert.Equal(7, next.RuntimeBindings.Count);
        foreach ((string type, string json) in OpenVrBuiltInBindings.Bindings)
        {
            string persisted = await File.ReadAllTextAsync(Path.Combine(saved, OpenVrInputManifest.ApplicationKey + "_" + type + ".json"));
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(persisted)));
        }
        next.Commit();
    }

    [Theory]
    [InlineData(OpenVrControllerHand.Left, "/user/hand/left/input/grip")]
    [InlineData(OpenVrControllerHand.Right, "/user/hand/right/input/grip")]
    public async Task ResetPreservesTheExistingRuntimeHandBasis(OpenVrControllerHand basis, string expectedGrip)
    {
        using TestDirectory directory = new();
        OpenVrBindingDefaults store = OpenVrBindingDefaults.CreateBuiltIn(Path.Combine(directory.Path, "saved"),
            Path.Combine(directory.Path, "runtime"), basis);
        await using IBindingDefaultsTransaction transaction = await store.BeginResetAsync(CancellationToken.None);
        JsonNode binding = JsonNode.Parse(await File.ReadAllTextAsync(transaction.RuntimeBindings["pico_controller"]))!;
        JsonArray sources = binding["bindings"]!["/actions/main"]!["sources"]!.AsArray();
        Assert.Contains(sources, source => source?["path"]?.GetValue<string>() == expectedGrip &&
            source["inputs"]?["click"]?["output"]?.GetValue<string>() == "/actions/main/in/phoneoverlaygrab");
        transaction.Commit();
    }

    [Fact]
    public void CompiledDefinitionsPreserveEveryExistingDefaultMapping()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, ".git"))) { root = Path.GetDirectoryName(root); }
        Assert.NotNull(root);
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "action_manifest.json")))!;
        Assert.True(JsonNode.DeepEquals(manifest, JsonNode.Parse(OpenVrBuiltInBindings.ManifestJson)));
        foreach (JsonNode? entry in manifest["default_bindings"]!.AsArray())
        {
            string type = entry!["controller_type"]!.GetValue<string>();
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(OpenVrBuiltInBindings.Bindings[type]),
                JsonNode.Parse(File.ReadAllText(Path.Combine(root, entry["binding_url"]!.GetValue<string>())))));
        }
    }

    internal sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "compiled-binding-tests", Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            if (Directory.Exists(Path)) { Directory.Delete(Path, recursive: true); }
        }
    }
}
