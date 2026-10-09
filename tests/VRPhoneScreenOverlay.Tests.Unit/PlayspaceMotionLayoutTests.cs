using System.Drawing;
using System.Windows.Forms;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PlayspaceMotionLayoutTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void MotionLabelsAreReadableAndNeverCoveredBySiblingControls(float scale)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using PlayspaceMotionView view = new();
                using MainShellView shell = new();
                shell.PageHost.Controls.Add(view);
                using UiScaleLayout layout = new(shell);
                layout.Apply(scale);
                Check(view);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    private static void Check(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (!control.Visible) { continue; }
            if (control is Label label && !string.IsNullOrWhiteSpace(label.Text))
            {
                Size text = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                Assert.True(text.Height <= label.Height, $"Clipped label: {label.Name}");
                foreach (Control other in parent.Controls)
                {
                    if (other == label || !other.Visible) { continue; }
                    Assert.False(label.Bounds.IntersectsWith(other.Bounds), $"{label.Name} overlaps {other.Name}");
                }
            }
            Check(control);
        }
    }
}
