using System.Security.Cryptography;
using System.Text.Json;
using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ScrcpyServerResourceValidatorTests
{
    [Fact]
    public async Task AcceptsServerMatchingManifestAsSingleHashSource()
    {
        using TemporaryServerDirectory directory = new([1, 2, 3, 4]);

        string result = await ScrcpyServerResourceValidator.ValidateAsync(
            directory.ServerPath,
            CancellationToken.None);

        Assert.Equal(directory.ServerPath, result);
    }

    [Fact]
    public async Task RejectsServerChangedAfterManifestWasWritten()
    {
        using TemporaryServerDirectory directory = new([1, 2, 3, 4]);
        await File.WriteAllBytesAsync(directory.ServerPath, [4, 3, 2, 1]);

        AndroidConnectionException exception = await Assert.ThrowsAsync<AndroidConnectionException>(
            () => ScrcpyServerResourceValidator.ValidateAsync(
                    directory.ServerPath,
                    CancellationToken.None)
                .AsTask());

        Assert.Equal("ANDROID_SCRCPY_SERVER_HASH_MISMATCH", exception.ReasonCode);
    }

    [Fact]
    public async Task RejectsMissingManifest()
    {
        using TemporaryServerDirectory directory = new([1, 2, 3, 4]);
        File.Delete(directory.ManifestPath);

        AndroidConnectionException exception = await Assert.ThrowsAsync<AndroidConnectionException>(
            () => ScrcpyServerResourceValidator.ValidateAsync(
                    directory.ServerPath,
                    CancellationToken.None)
                .AsTask());

        Assert.Equal("ANDROID_SCRCPY_MANIFEST_MISSING", exception.ReasonCode);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"customization\":{\"guardedWakeControlMessageType\":128},\"files\":{}}")]
    [InlineData("{\"customization\":{\"guardedWakeControlMessageType\":128},\"files\":{\"scrcpy-server-v4.1\":\"not-a-hash\"}}")]
    public async Task RejectsInvalidManifest(string manifest)
    {
        using TemporaryServerDirectory directory = new([1, 2, 3, 4]);
        await File.WriteAllTextAsync(directory.ManifestPath, manifest);

        AndroidConnectionException exception = await Assert.ThrowsAsync<AndroidConnectionException>(
            () => ScrcpyServerResourceValidator.ValidateAsync(
                    directory.ServerPath,
                    CancellationToken.None)
                .AsTask());

        Assert.Equal("ANDROID_SCRCPY_MANIFEST_INVALID", exception.ReasonCode);
    }

    [Fact]
    public async Task RejectsOldServerEvenWhenItsOwnManifestHashMatches()
    {
        using TemporaryServerDirectory directory = new([1, 2, 3, 4], false);
        AndroidConnectionException exception = await Assert.ThrowsAsync<AndroidConnectionException>(
            () => ScrcpyServerResourceValidator.ValidateAsync(directory.ServerPath, CancellationToken.None).AsTask());
        Assert.Equal("ANDROID_SCRCPY_MANIFEST_INVALID", exception.ReasonCode);
    }

    private sealed class TemporaryServerDirectory : IDisposable
    {
        public TemporaryServerDirectory(byte[] payload, bool supportsGuardedWake = true)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vrpso-scrcpy-validator-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            File.WriteAllBytes(ServerPath, payload);
            string hash = Convert.ToHexStringLower(SHA256.HashData(payload));
            File.WriteAllText(
                ManifestPath,
                JsonSerializer.Serialize(new
                {
                    customization = supportsGuardedWake
                        ? new Dictionary<string, int> { ["guardedWakeControlMessageType"] = 128 }
                        : [],
                    files = new Dictionary<string, string>
                    {
                        [System.IO.Path.GetFileName(ServerPath)] = hash,
                    },
                }));
        }

        public string Path { get; }

        public string ServerPath => System.IO.Path.Combine(Path, "scrcpy-server-v4.1");

        public string ManifestPath => System.IO.Path.Combine(Path, "manifest.json");

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
