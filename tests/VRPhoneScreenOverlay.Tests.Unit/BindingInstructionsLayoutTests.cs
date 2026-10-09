using System.Drawing;
using System.Windows.Forms;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class BindingInstructionsLayoutTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void PhoneAndPlayspaceInstructionsHaveSeparateFramesAndRestoreFitsBetweenHandAndBindingButtons(float scale)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using Font font = new("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
                using MainShellView shell = new() { Font = font };
                using ControllerBindingsView page = new();
                shell.PageHost.Controls.Add(page);
                page.Location = UiLayoutMetrics.PageLocation;
                page.HandSelector.SetNodes([new(ControllerHandPreference.Left, "左手"), new(ControllerHandPreference.Right, "右手")], ControllerHandPreference.Right);
                ControllerBindingGuide guide = OpenVrBindingGuide.ReadDefaultAsync("pico_controller", OpenVrControllerHand.Right, CancellationToken.None).AsTask().GetAwaiter().GetResult();
                page.SetGuide(guide);
                shell.CreateControl();
                using UiScaleLayout layout = new(shell);
                layout.Apply(scale);
                SurfacePanel phone = (SurfacePanel)page.Controls.Find("phoneInstructionsPanel", true).Single();
                SurfacePanel space = (SurfacePanel)page.Controls.Find("playspaceInstructionsPanel", true).Single();
                Assert.True(phone.Visible);
                Assert.True(space.Visible);
                Assert.True(space.Bottom <= space.Parent!.ClientSize.Height, "Both groups must be visible in the normal page.");
                Assert.False(((ScrollableControl)space.Parent).HorizontalScroll.Visible);
                Assert.False(((ScrollableControl)space.Parent).VerticalScroll.Visible);
                Assert.InRange(space.Top - phone.Bottom, (int)(6 * scale), (int)(12 * scale));
                Assert.DoesNotContain(Descendants(phone).OfType<Label>(), label => label.Text.Contains("空间拖拽", StringComparison.Ordinal));
                Assert.Contains(Descendants(space).OfType<Label>(), label => label.Text.StartsWith("空间拖拽", StringComparison.Ordinal));
                Assert.Contains(Descendants(phone).OfType<Label>(), label => label.Text.StartsWith("截屏", StringComparison.Ordinal));
                Assert.True(page.HandSelector.Right < page.RestoreBindingsButton.Left);
                Assert.True(page.RestoreBindingsButton.Right < page.OpenBindingsButton.Left);
                foreach (Control button in new Control[] { page.RestoreBindingsButton, page.OpenBindingsButton, page.HandSelector })
                { Assert.True(page.ClientRectangle.Contains(button.Bounds)); }
                Assert.Equal("恢复默认绑定", page.RestoreBindingsButton.Text);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) { yield return nested; }
        }
    }
}
