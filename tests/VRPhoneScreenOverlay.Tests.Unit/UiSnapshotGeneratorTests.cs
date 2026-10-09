using System.Drawing;
using System.Text.Json;
using VRPhoneScreenOverlay.App;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class UiSnapshotGeneratorTests
{
    [Fact]
    public void EveryUiScenarioRendersAtTheMainWindowClientSize()
    {
        string output = Path.Combine(
            Path.GetTempPath(),
            "VRPhoneScreenOverlay.Tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            int exitCode = -1;
            Exception? failure = null;
            Thread renderThread = new(() =>
            {
                try
                {
                    exitCode = UiSnapshotGenerator.Run(output);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            renderThread.SetApartmentState(ApartmentState.STA);
            renderThread.Start();
            Assert.True(renderThread.Join(TimeSpan.FromSeconds(60)), "UI snapshot rendering timed out.");
            Assert.Null(failure);
            Assert.Equal(0, exitCode);

            foreach ((string directory, float scale) in new[] { ("", 1f), ("1080p-150", 1.5f), ("4k-200", 2f) })
            {
                foreach (string fileName in UiSnapshotGenerator.ExpectedFileNames)
                {
                    string path = Path.Combine(output, directory, fileName);
                    Assert.True(File.Exists(path), $"Missing UI snapshot: {path}");
                    using Image image = Image.FromFile(path);
                    Assert.Equal(UiLayoutMetrics.ClientSizeFor(scale).Width, image.Width);
                    Assert.Equal(UiLayoutMetrics.ClientSizeFor(scale).Height, image.Height);
                }
            }

            using JsonDocument manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(output, "manifest.json")));
            Assert.Equal(
                UiSnapshotGenerator.ExpectedFileNames.Length,
                manifest.RootElement.GetProperty("files").GetArrayLength());
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }
}
