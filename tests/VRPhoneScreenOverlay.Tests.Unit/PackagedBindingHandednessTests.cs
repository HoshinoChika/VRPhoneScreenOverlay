using System.Text.Json;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PackagedBindingHandednessTests
{
    [Fact]
    public void EveryPackagedProfileGeneratesCoherentLeftHandPhoneControls()
    {
        string repository = FindRepositoryRoot();
        string temporary = Path.Combine(
            Path.GetTempPath(),
            "VRPhoneScreenOverlay.Tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            OpenVrBindingProfileStore store = new(
                Path.Combine(repository, "action_manifest.json"),
                Path.Combine(temporary, "saved"),
                Path.Combine(temporary, "runtime"),
                OpenVrControllerHand.Left);

            OpenVrPreparedInputManifest prepared = store.Prepare();

            Assert.Equal(7, prepared.Bindings.Count);
            foreach (OpenVrPreparedBindingProfile profile in prepared.Bindings)
            {
                using JsonDocument document = JsonDocument.Parse(
                    File.ReadAllText(profile.RuntimePath));
                List<(string Path, HashSet<string> Outputs)> sources = [];
                CollectSources(document.RootElement, sources);
                (string Path, HashSet<string> Outputs)[] phoneSources = sources
                    .Where(source => source.Outputs.Any(IsPhoneAction))
                    .ToArray();
                (string Path, HashSet<string> Outputs)[] legacySources = sources
                    .Where(source => source.Outputs.Any(IsPlayspaceAction))
                    .ToArray();

                Assert.NotEmpty(phoneSources);
                Assert.NotEmpty(legacySources);
                Assert.All(phoneSources, source =>
                    Assert.StartsWith("/user/hand/left/", source.Path));
                Assert.All(legacySources, source =>
                    Assert.StartsWith("/user/hand/right/", source.Path));

                if (profile.ControllerType is
                    "oculus_touch" or "pico_controller" or "hpmotioncontroller")
                {
                    Assert.DoesNotContain(phoneSources, source =>
                        source.Path is "/user/hand/left/input/a" or
                            "/user/hand/left/input/b");
                    Assert.DoesNotContain(legacySources, source =>
                        source.Path is "/user/hand/right/input/x" or
                            "/user/hand/right/input/y");
                }
            }
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static void CollectSources(
        JsonElement element,
        ICollection<(string Path, HashSet<string> Outputs)> sources)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("path", out JsonElement pathElement) &&
                pathElement.ValueKind == JsonValueKind.String &&
                pathElement.GetString() is string path)
            {
                HashSet<string> outputs = new(StringComparer.OrdinalIgnoreCase);
                CollectOutputs(element, outputs);
                if (outputs.Count > 0)
                {
                    sources.Add((path, outputs));
                }
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                CollectSources(property.Value, sources);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                CollectSources(child, sources);
            }
        }
    }

    private static void CollectOutputs(JsonElement element, ISet<string> outputs)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "output", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() is string output)
                {
                    outputs.Add(output);
                }

                CollectOutputs(property.Value, outputs);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                CollectOutputs(child, outputs);
            }
        }
    }

    private static bool IsPhoneAction(string output) =>
        output.StartsWith("/actions/main/in/phone", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("/actions/phonebuttons/in/phone", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("/actions/phonebuttonstate/in/", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("/actions/pointer/in/phone", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlayspaceAction(string output) =>
        output.EndsWith("spacedrag", StringComparison.OrdinalIgnoreCase) ||
        output.Equals("/actions/main/in/resetoffsets", StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "action_manifest.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "bindings")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate repository root.");
    }
}
