using VRPhoneScreenOverlay.App.Views;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class CloseConfirmationLayerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfirmationBlocksCoveredControlsAndRestoresTheirPreviousState(bool enabled)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using MainShellView shell = new();
                shell.PageHost.Enabled = enabled;
                shell.CloseConfirmation.Visible = true;
                Assert.False(shell.PageHost.Enabled);
                Assert.False(shell.HomeNavigation.Enabled);
                Assert.True(shell.CloseConfirmation.MinimizeButton.Enabled);
                Assert.True(shell.CloseConfirmation.ExitButton.Enabled);
                shell.CloseConfirmation.Visible = false;
                Assert.Equal(enabled, shell.PageHost.Enabled);
                Assert.True(shell.HomeNavigation.Enabled);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
