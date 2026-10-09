using System.Windows.Forms;
using VRPhoneScreenOverlay.App;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class MotionValueControlTests
{
    [Fact]
    public void HoldRepeatWaitsAcceleratesAndStopsWithoutCatchingUpMissedTicks()
    {
        HoldRepeatState hold = new();
        hold.Start(0);
        Assert.Equal(0, hold.Poll(399));
        Assert.Equal(1, hold.Poll(400));
        Assert.Equal(0, hold.Poll(430));
        Assert.Equal(1, hold.Poll(460));
        Assert.Equal(5, hold.Poll(2000));
        Assert.Equal(0, hold.Poll(2001));
        hold.Stop();
        Assert.Equal(0, hold.Poll(10000));
    }

    [Fact]
    public void RepeatStepsAccelerateAndNumericValuesClampAtBothEnds()
    {
        Assert.Equal(1, RepeatingStepButton.RepeatUnits(399));
        Assert.Equal(1, RepeatingStepButton.RepeatUnits(1999));
        Assert.Equal(5, RepeatingStepButton.RepeatUnits(2000));
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using MotionValueControl control = new() { Minimum = 0, Maximum = 20, Increment = 0.1m, Value = 1 };
                int changed = 0;
                control.ValueChanged += (_, _) => changed++;
                control.Adjust(5);
                Assert.Equal(1.5m, control.Value);
                control.Adjust(1000);
                Assert.Equal(20, control.Value);
                control.Adjust(5);
                Assert.Equal(2, changed);
                control.Adjust(-1000);
                Assert.Equal(0, control.Value);
                control.SelectValue(5);
                Assert.Equal(3, changed); // Runtime/state synchronization does not cause a new save.
                TextBox editor = control.Controls.OfType<SurfacePanel>().Single().Controls.OfType<TextBox>().Single();
                editor.Text = "7";
                control.SelectValue(5);
                Assert.Equal("7", editor.Text); // An unchanged async save notification cannot erase a draft.
                control.Commit();
                Assert.Equal(7, control.Value);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
