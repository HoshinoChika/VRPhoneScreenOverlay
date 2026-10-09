using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Core;
using VRPhoneScreenOverlay.Media;
using VRPhoneScreenOverlay.Sharing;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AppRuntimeTests
{
    [Fact]
    public async Task RuntimeStartsAndStopsThroughExplicitStates()
    {
        AppRuntime runtime = new();
        List<AppLifecycleState> states = [];
        runtime.StateChanged += (_, args) => states.Add(args.Current.State);

        await runtime.StartAsync(CancellationToken.None);
        await runtime.StopAsync(CancellationToken.None);
        await runtime.DisposeAsync();

        Assert.Equal(
            [
                AppLifecycleState.Starting,
                AppLifecycleState.Ready,
                AppLifecycleState.Stopping,
                AppLifecycleState.Stopped,
            ],
            states);
    }

    [Fact]
    public async Task NullSharingSessionRejectsEnabledSharing()
    {
        NullSharingSession session = new();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.StartAsync(
                SharingMode.FreePeerToPeer,
                SharingRole.Host,
                CancellationToken.None));

        await session.DisposeAsync();
    }

    [Fact]
    public void InitialSharingProfilesStayWithin1080pAndBitratePolicy()
    {
        ShareVideoProfile.InitialLandscape.Validate();
        ShareVideoProfile.InitialPortrait.Validate();

        Assert.Equal(1920, ShareVideoProfile.InitialLandscape.MaximumWidth);
        Assert.Equal(1080, ShareVideoProfile.InitialLandscape.MaximumHeight);
        Assert.Equal(1080, ShareVideoProfile.InitialPortrait.MaximumWidth);
        Assert.Equal(1920, ShareVideoProfile.InitialPortrait.MaximumHeight);
        Assert.Equal(8_000_000, ShareVideoProfile.InitialLandscape.MaximumBitrateBitsPerSecond);
    }
}
