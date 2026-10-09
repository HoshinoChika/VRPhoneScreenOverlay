using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.Diagnostics;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class DetailedRecheckTests
{
    [Theory]
    [InlineData("{\"token\":\"example-private-value\"")]
    [InlineData("{\"token\":\"example-private-value\", broken}")]
    [InlineData("{\"token\":\"example-private-value\",\"token\":\"second-private-value\"}")]
    public void DetailedRecheckMalformedDiagnosticLinesCannotBypassRedaction(string line)
    {
        string result = DiagnosticSanitizer.SanitizeJsonLine(line);
        Assert.DoesNotContain("private-value", result, StringComparison.Ordinal);
        Assert.NotNull(System.Text.Json.Nodes.JsonNode.Parse(result));
    }

    [Fact]
    public void DetailedRecheckBusyWirelessPanelPreventsConflictingRequests()
    {
        OnUiThread(() =>
        {
            using WirelessConnectionView view = new();
            view.DeviceList.Items.Add("Example USB phone");
            view.SetBusy(true);
            Assert.False(view.RefreshButton.Enabled);
            Assert.False(view.DeviceList.Enabled);
            Assert.True(view.CancelButton.Enabled);
            Assert.True(view.BackButton.Enabled);
            view.SetBusy(false);
            Assert.True(view.RefreshButton.Enabled);
            Assert.False(view.CancelButton.Enabled);
        });
    }

    [Theory]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, true, false, true, true)]
    [InlineData(false, true, false, false, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(false, false, false, false, false)]
    public void DetailedRecheckPendingWirelessBlocksNewOverlayButAllowsExistingStop(
        bool canStop, bool ready, bool busy, bool wirelessBusy, bool expected)
    {
        OnUiThread(() =>
        {
            using ModernButton button = new();
            PhoneOverlayButtonPolicy.Apply(button, canStop, ready, busy, wirelessBusy);
            Assert.Equal(expected, button.Enabled);
        });
    }

    private static void OnUiThread(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "UI check did not complete.");
        if (failure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure); }
    }
}
