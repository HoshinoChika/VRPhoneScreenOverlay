using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace VRPhoneScreenOverlay.Tests.Architecture;

public sealed class CompiledUiBoundaryTests
{
    [Fact]
    public void UiCannotBypassPlatformBoundaryUsingAliasesOrShortTypeNames()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) { root = root.Parent; }
        Assert.NotNull(root);
        string project = Path.Combine(root.FullName, "src", "VRPhoneScreenOverlay.App");
        XDocument definition = XDocument.Load(Path.Combine(project, "VRPhoneScreenOverlay.App.csproj"));
        string framework = definition.Descendants("TargetFramework").Single().Value;
        string configuration = typeof(CompiledUiBoundaryTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string assembly = Path.Combine(project, "bin", configuration, framework, "win-x64", "VRPhoneScreenOverlay.dll");
        Assert.True(File.Exists(assembly), "Build the solution before checking compiled boundaries.");
        using FileStream stream = File.OpenRead(assembly);
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            TypeReference type = metadata.GetTypeReference(handle);
            string name = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
            Assert.False(name.StartsWith("Valve.VR.", StringComparison.Ordinal), name);
            Assert.False(name is "System.Net.Http.HttpClient" or "System.Diagnostics.Process" or "System.IO.FileSystemWatcher", name);
        }
    }
}
