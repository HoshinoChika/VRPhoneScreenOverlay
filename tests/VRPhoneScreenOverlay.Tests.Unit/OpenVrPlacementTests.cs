using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPlacementTests
{
    [Fact]
    public void GameRootTransformCancelsOutOfRelativeViewWhilePhysicalHeadMotionDoesNot()
    {
        HmdMatrix34_t head = OpenVrTransformMath.Identity();
        head.m7 = 1.7f;
        HmdMatrix34_t phone = OpenVrPhonePlacement.InFrontOf(head, 1.1f);
        HmdMatrix34_t gameRoot = OpenVrTransformMath.Identity();
        gameRoot.m0 = 0; gameRoot.m2 = 1; gameRoot.m8 = -1; gameRoot.m10 = 0;
        gameRoot.m3 = 20; gameRoot.m11 = -30;
        HmdMatrix34_t relative = OpenVrTransformMath.Multiply(
            OpenVrTransformMath.InverseRigid(OpenVrTransformMath.Multiply(gameRoot, head)),
            OpenVrTransformMath.Multiply(gameRoot, phone));
        Assert.Equal(0, relative.m3, 4);
        Assert.Equal(-1.1f, relative.m11, 4);
        head.m3 = 0.5f;
        HmdMatrix34_t afterPhysicalStep = OpenVrTransformMath.Multiply(OpenVrTransformMath.InverseRigid(head), phone);
        Assert.Equal(-0.5f, afterPhysicalStep.m3, 4);
    }
    [Fact]
    public void EveryAppearanceUsesTheCurrentHeadAndDefaultDistance()
    {
        HmdMatrix34_t head = OpenVrTransformMath.Identity();
        head.m3 = 2;
        HmdMatrix34_t first = OpenVrPhonePlacement.InFrontOf(head, 1.1f);
        Assert.Equal(2, first.m3);
        Assert.Equal(-1.1f, first.m11);
        head.m3 = 4;
        head.m0 = 0;
        head.m2 = 1;
        head.m8 = -1;
        head.m10 = 0;
        HmdMatrix34_t restored = OpenVrPhonePlacement.InFrontOf(head, 1.1f);
        Assert.Equal(2.9f, restored.m3, 4);
        Assert.Equal(0, restored.m11, 4);
    }

    [Fact]
    public async Task PlacementIsAtomicallySavedAndCorruptValuesFallBack()
    {
        string directory = Path.Combine(Path.GetTempPath(), "VRPhoneScreenPlacement-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "placement.json");
        Directory.CreateDirectory(directory);
        try
        {
            using (OpenVrPlacementStore store = new(path))
            {
                store.Queue(new OpenVrSavedPlacement(1.4f));
            }

            OpenVrSavedPlacement? saved = await OpenVrPlacementStore.LoadAsync(path, CancellationToken.None);
            Assert.NotNull(saved);
            Assert.Equal(1.4f, saved.Scale);
            Assert.DoesNotContain("Relative", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(directory));
            await File.WriteAllTextAsync(path, "{broken}");
            Assert.Null(await OpenVrPlacementStore.LoadAsync(path, CancellationToken.None));
            Assert.False(new OpenVrSavedPlacement(float.NaN).IsValid);
            Assert.False(new OpenVrSavedPlacement(1, SchemaVersion: 99).IsValid);
            await File.WriteAllTextAsync(path, "{\"Scale\":1.8,\"Relative\":[999,999,999],\"SchemaVersion\":1}");
            saved = await OpenVrPlacementStore.LoadAsync(path, CancellationToken.None);
            Assert.NotNull(saved);
            Assert.Equal(1.8f, saved.Scale);
            Assert.Equal(2, saved.SchemaVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
