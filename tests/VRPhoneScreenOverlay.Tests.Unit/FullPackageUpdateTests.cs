using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Maintenance;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Update;
using TestDirectory = VRPhoneScreenOverlay.Tests.Unit.CompiledBindingDefaultsTests.TestDirectory;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class FullPackageUpdateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SameFullZipStagesAndReplacesTheWholeInstallationWithHealthRollback(bool healthy)
    {
        using TestDirectory directory = new();
        string install = Path.Combine(directory.Path, "installed");
        string packageSource = Path.Combine(directory.Path, "package");
        UpdateInstallLayoutTests.CreateFullInstallation(install);
        UpdateInstallLayoutTests.CreateFullInstallation(Path.Combine(packageSource, AppIdentity.LocalDataFolderName));
        await File.WriteAllTextAsync(Path.Combine(install, "app", "VRPhoneScreenOverlay.dll"), "old");
        string config = JsonSerializer.Serialize(new { SchemaVersion = 1, Client = TestServiceConfiguration.Value });
        await File.WriteAllTextAsync(Path.Combine(install, "app", "service.config.json"), config);
        byte[] package = CreateArchive(directory.Path, packageSource);
        UpdateRelease release = Release(package);
        using PackageClient client = new(package);
        PreparedUpdate prepared = await UpdatePackageStager.DownloadAndStageAsync(client, release, 1024 * 1024,
            TimeSpan.FromSeconds(2), null, CancellationToken.None,
            new(Path.Combine(directory.Path, "downloads"), install));
        Assert.Equal(TestServiceConfiguration.Value, ClientServiceConfiguration.Load(Path.Combine(prepared.StageDirectory, "app", "service.config.json")).Configuration);
        Assert.Equal("synthetic", await File.ReadAllTextAsync(Path.Combine(prepared.StageDirectory, "app", "VRPhoneScreenOverlay.dll")));
        int result = UpdateDirectoryTransaction.Apply(install, prepared.StageDirectory, prepared.TransactionToken, () => healthy, () => { }, () => { });
        Assert.Equal(healthy ? 0 : 4, result);
        Assert.Equal(healthy ? "synthetic" : "old", await File.ReadAllTextAsync(Path.Combine(install, "app", "VRPhoneScreenOverlay.dll")));
        Assert.Equal(TestServiceConfiguration.Value, ClientServiceConfiguration.Load(Path.Combine(install, "app", "service.config.json")).Configuration);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("flat")]
    [InlineData("traversal")]
    public async Task InvalidPackagesNeverChangeTheExistingInstallation(string failure)
    {
        using TestDirectory directory = new();
        string install = Path.Combine(directory.Path, "installed");
        string packageSource = Path.Combine(directory.Path, "package");
        UpdateInstallLayoutTests.CreateFullInstallation(install);
        UpdateInstallLayoutTests.CreateFullInstallation(failure == "flat" ? packageSource : Path.Combine(packageSource, AppIdentity.LocalDataFolderName));
        await File.WriteAllTextAsync(Path.Combine(install, "app", "VRPhoneScreenOverlay.dll"), "old");
        byte[] package = CreateArchive(directory.Path, packageSource);
        if (failure == "traversal")
        {
            using MemoryStream stream = new();
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create, true))
            {
                await using Stream entry = await zip.CreateEntry("../outside.txt").OpenAsync(CancellationToken.None);
                entry.WriteByte(1);
            }
            package = stream.ToArray();
        }
        UpdateRelease release = Release(package);
        if (failure == "hash") { release = release with { PackageSha256 = new string('a', 64) }; }
        using PackageClient client = new(package);
        await Assert.ThrowsAsync<UpdateException>(() => UpdatePackageStager.DownloadAndStageAsync(client, release, 1024 * 1024,
            TimeSpan.FromSeconds(2), null, CancellationToken.None, new(Path.Combine(directory.Path, "downloads"), install)).AsTask());
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(install, "app", "VRPhoneScreenOverlay.dll")));
        Assert.False(File.Exists(Path.Combine(directory.Path, "outside.txt")));
        Assert.Empty(Directory.GetDirectories(directory.Path, ".VRPhoneScreenOverlay-stage-*"));
    }

    private static UpdateRelease Release(byte[] package) => new("1.0.0", DateTimeOffset.UtcNow,
        new("https://service.invalid/vrphonescreen/api/v1/updates/download/1.0.0"), package.Length,
        Convert.ToHexStringLower(SHA256.HashData(package)), "synthetic");

    private static byte[] CreateArchive(string root, string source)
    {
        string archive = Path.Combine(root, "package.zip");
        ZipFile.CreateFromDirectory(source, archive);
        return File.ReadAllBytes(archive);
    }

    private sealed class PackageClient(byte[] bytes) : IAppHttpClient
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "IAppHttpClient transfers response ownership to the production stager, which disposes it.")]
        public ValueTask<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, NetworkRetryPolicy retryPolicy,
            HttpCompletionOption completionOption, Func<HttpStatusCode, bool>? isSuccess, CancellationToken cancellationToken)
            => ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        public void Dispose() { }
    }
}
