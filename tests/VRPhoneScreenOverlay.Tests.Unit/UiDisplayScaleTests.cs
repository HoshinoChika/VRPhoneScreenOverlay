using System.Drawing;
using System.Windows.Forms;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class UiDisplayScaleTests
{
    private static readonly float[] _monitorScales = [1f, 2f, 1.5f, 1.3631873f, 1f, 2f, 1f];
    [Theory]
    [InlineData(1920, 1080, 96, 1)]
    [InlineData(1280, 720, 96, 1)]
    [InlineData(1366, 768, 144, 1.3631873f)]
    [InlineData(1920, 1080, 144, 1.5f)]
    [InlineData(2560, 1440, 96, 1.3333333f)]
    [InlineData(3840, 2160, 96, 2)]
    [InlineData(3840, 2160, 144, 2)]
    [InlineData(3840, 2160, 192, 2)]
    public void ResolutionAndDpiDetermineOneSharedScale(int width, int height, int dpi, float expected) =>
        Assert.Equal(expected, UiLayoutMetrics.DisplayScale(new Size(width, height), new Size(width, height - 40), dpi), 4);

    [Fact]
    public void SmallWorkingAreaKeepsTheWholeWindowReachable()
    {
        Size work = new(800, 560);
        float scale = UiLayoutMetrics.DisplayScale(new Size(1920, 1080), work, 288);
        Size window = UiLayoutMetrics.ClientSizeFor(scale);
        Assert.True(window.Width < work.Width && window.Height < work.Height);
    }

    [Fact]
    public void TextFitsAndMonitorRoundTripsDoNotAccumulateScaling()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using MainShellView shell = new();
                using HomePageView home = new() { Location = UiLayoutMetrics.PageLocation };
                home.OpenOverlayButton.Text = "关闭手机浮窗";
                home.MetricResolution.Text = "1080×2400";
                shell.PageHost.Controls.Add(home);
                using UiScaleLayout layout = new(shell);
                using Form window = new() { AutoScaleMode = AutoScaleMode.None, FormBorderStyle = FormBorderStyle.None, MaximizeBox = false };
                window.Controls.Add(shell);
                Rectangle originalButton = home.OpenOverlayButton.Bounds;
                foreach (float scale in _monitorScales)
                {
                    layout.Apply(scale);
                    UiLayoutMetrics.LockWindowSize(window, scale);
                    Size expectedSize = window.Size;
                    window.Size = new Size(expectedSize.Width + 100, expectedSize.Height + 100);
                    Assert.Equal(expectedSize, window.Size);
                    Assert.Equal(window.MinimumSize, window.MaximumSize);
                    Assert.Equal(UiLayoutMetrics.ClientSizeFor(scale), window.ClientSize);
                    Assert.Equal(GraphicsUnit.Pixel, home.OpenOverlayButton.Font.Unit);
                    Size text = TextRenderer.MeasureText(home.OpenOverlayButton.Text, home.OpenOverlayButton.Font,
                        new Size(home.OpenOverlayButton.Width - UiLayoutMetrics.Pixels(home, 10), int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    Assert.True(text.Height <= home.OpenOverlayButton.Height / 2 - UiLayoutMetrics.Pixels(home, 5),
                        $"Button text clips at scale {scale}: height {text.Height}.");
                    Size resolutionText = TextRenderer.MeasureText(home.MetricResolution.Text, home.MetricResolution.Font);
                    Assert.True(resolutionText.Width <= home.MetricResolution.Width);
                    if (scale == 1) { Assert.Equal(originalButton, home.OpenOverlayButton.Bounds); }
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
