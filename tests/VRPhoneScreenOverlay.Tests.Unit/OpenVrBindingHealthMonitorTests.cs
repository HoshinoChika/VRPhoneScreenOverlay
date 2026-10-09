using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrBindingHealthMonitorTests
{
    [Fact]
    public void LoadingNoticeAppearsOnlyAfterOneSecond()
    {
        OpenVrBindingHealthMonitor monitor = new();

        monitor.Begin(1_000);
        monitor.Advance(1_999);
        Assert.False(monitor.Snapshot.ShowNotice);

        monitor.Advance(2_000);
        Assert.True(monitor.Snapshot.ShowNotice);
        Assert.Equal(OpenVrBindingHealthState.Loading, monitor.Snapshot.State);
    }

    [Fact]
    public void CompleteRequiredBindingsBecomeReadySilently()
    {
        OpenVrBindingHealthMonitor monitor = new();

        monitor.Begin(1_000);
        monitor.RecordProbe(1_100, true, true);

        Assert.Equal(OpenVrBindingHealthState.Ready, monitor.Snapshot.State);
        Assert.False(monitor.Snapshot.ShowNotice);
    }

    [Fact]
    public void ThreeInitializationFailuresShowRecoveryNotice()
    {
        OpenVrBindingHealthMonitor monitor = new();

        monitor.Begin(1_000);
        monitor.RecordInitializationFailure(2_000);
        monitor.RecordInitializationFailure(3_000);
        Assert.Equal(OpenVrBindingHealthState.Loading, monitor.Snapshot.State);

        monitor.RecordInitializationFailure(4_000);
        Assert.Equal(OpenVrBindingHealthState.Failed, monitor.Snapshot.State);
        Assert.True(monitor.Snapshot.ShowNotice);
    }

    [Fact]
    public void MissingControllerDoesNotCountAsBindingFailure()
    {
        OpenVrBindingHealthMonitor monitor = new();

        monitor.Begin(1_000);
        monitor.ObserveBindingLoadFailed(2_000);
        monitor.RecordProbe(2_000, false, false);
        monitor.RecordProbe(3_000, false, false);
        monitor.RecordProbe(4_000, false, false);

        Assert.Equal(OpenVrBindingHealthState.Loading, monitor.Snapshot.State);
    }

    [Fact]
    public void SuccessfulLateProbeClosesFailureNotice()
    {
        OpenVrBindingHealthMonitor monitor = new();

        monitor.Begin(1_000);
        monitor.RecordInitializationFailure(2_000);
        monitor.RecordInitializationFailure(3_000);
        monitor.RecordInitializationFailure(4_000);
        monitor.RecordProbe(5_000, true, true);

        Assert.Equal(OpenVrBindingHealthState.Ready, monitor.Snapshot.State);
        Assert.False(monitor.Snapshot.ShowNotice);
    }

    [Fact]
    public void ReadyBindingsAreNotProbedAgain()
    {
        OpenVrBindingHealthMonitor monitor = new();

        monitor.Begin(1_000);
        monitor.RecordProbe(1_100, true, true);

        Assert.False(monitor.ShouldProbe(2_099));
        Assert.False(monitor.ShouldProbe(2_100));
    }

    [Fact]
    public void RuntimeLoadFailureDoesNotDowngradeReadyBinding()
    {
        OpenVrBindingHealthMonitor monitor = new();

        monitor.Begin(1_000);
        monitor.RecordProbe(1_100, true, true);
        monitor.ObserveBindingLoadFailed(10_000);

        Assert.Equal(OpenVrBindingHealthState.Ready, monitor.Snapshot.State);
        monitor.RecordProbe(10_000, true, false);
        monitor.RecordProbe(11_000, true, false);
        monitor.RecordProbe(12_000, true, false);

        Assert.Equal(OpenVrBindingHealthState.Ready, monitor.Snapshot.State);
        Assert.False(monitor.Snapshot.ShowNotice);
    }
}
