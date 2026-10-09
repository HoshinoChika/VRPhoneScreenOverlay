using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPlayspaceDragControllerTests
{
    [Fact]
    public void RuntimeDefaultsToDisabledAtOneTimes()
    {
        using OpenVrPlayspaceDragService service = new(OpenVrControllerHand.Right, 1f);

        Assert.False(service.Snapshot.Enabled);
        Assert.Equal(1f, service.Snapshot.Multiplier);
    }

    [Fact]
    public void MultiplierChangeIsImmediateAndRecorded()
    {
        using OpenVrPlayspaceDragService service = new(OpenVrControllerHand.Right, 1f);
        OpenVrPlayspaceDragDiagnostic? observed = null;
        service.DiagnosticRecorded += (_, eventArgs) => observed = eventArgs.Diagnostic;

        service.SetMultiplier(20f);

        Assert.Equal(20f, service.Snapshot.Multiplier);
        Assert.NotNull(observed);
        Assert.Equal("PLAYSPACE_DRAG_MULTIPLIER_APPLIED", observed.ReasonCode);
        Assert.Contains("applied=20", observed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeltaUsesConfiguredMultiplierOnEveryAxis()
    {
        (float x, float y, float z) = OpenVrPlayspaceDragController.CalculateDelta(
            3f,
            -1f,
            8f,
            1f,
            2f,
            4f,
            1.5f);

        Assert.Equal(3f, x);
        Assert.Equal(-4.5f, y);
        Assert.Equal(6f, z);
    }

    [Theory]
    [InlineData(1, 0.25f)]
    [InlineData(5, 1.25f)]
    [InlineData(10, 2.5f)]
    [InlineData(20, 5f)]
    [InlineData(40, 10f)]
    public void EveryVisibleMultiplierChangesAppliedMotion(
        int multiplier,
        float expected)
    {
        (float x, _, _) = OpenVrPlayspaceDragController.CalculateDelta(
            1.25f,
            0,
            0,
            1f,
            0,
            0,
            multiplier);

        Assert.Equal(expected, x);
    }

    [Theory]
    [InlineData(0.5f, -0.25f, 0.125f, true)]
    [InlineData(100f, 0f, 0f, false)]
    [InlineData(float.NaN, 0f, 0f, false)]
    [InlineData(float.PositiveInfinity, 0f, 0f, false)]
    public void SafetyGateRejectsInvalidOrImplausibleMotion(
        float x,
        float y,
        float z,
        bool expected)
    {
        Assert.Equal(expected, OpenVrPlayspaceDragController.IsSafeDelta(x, y, z));
    }

    [Fact]
    public void StandingOffsetIsRotatedIntoRawTrackingBasis()
    {
        HmdMatrix34_t basis = default;
        basis.m0 = 0;
        basis.m2 = 1;
        basis.m3 = 10;
        basis.m5 = 1;
        basis.m7 = 20;
        basis.m8 = -1;
        basis.m10 = 0;
        basis.m11 = 30;

        HmdMatrix34_t moved = OpenVrPlayspaceDragController.ApplyStandingOffset(
            basis,
            5,
            -2,
            8);

        Assert.Equal(18, moved.m3);
        Assert.Equal(18, moved.m7);
        Assert.Equal(25, moved.m11);
    }
}
