using System.Drawing;
using System.Drawing.Imaging;
using Valve.VR;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using VRPhoneScreenOverlay.SteamVR;
using static Vortice.Direct3D11.D3D11;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class MenuSpacingSliderTests
{
    [Fact]
    public void GapsKeepTheGearTargetWithoutTakingOverPhoneOrMenuControls()
    {
        using OpenVrControlTextureTests.OverlayBoundary boundary = new();
        using OpenVrPhoneMenuView view = CreateView(boundary);
        var state = Expanded();
        var phone = OpenVrTransformMath.Identity(); phone.m11 = -1;
        view.Render(state, phone, 0.3f, 0.5f, OpenVrTransformMath.Identity(), default);
        var dot = OpenVrPhoneMenuLayout.DotTransform(false, phone, 0.3f, 0.5f, phone);
        var panel = OpenVrPhoneMenuLayout.PanelTransform(dot);
        float left = dot.m3 - OpenVrPhoneMenuLayout.DotWidth / 2;
        float right = dot.m3 + OpenVrPhoneMenuLayout.DotWidth / 2;
        Assert.Equal(0.008f, panel.m3 - OpenVrPhoneMenuLayout.PanelWidth / 2 - right, 5);
        foreach (var point in new[] { (left - 0.004f, dot.m7), (right + 0.004f, dot.m7), (dot.m3, dot.m7 - 0.2f) })
        {
            Assert.True(view.TryPick(Pointer(point.Item1, point.Item2), false, out var hit));
            Assert.Equal(OpenVrMenuTarget.Dot, hit.Target);
            Assert.Equal(OpenVrPhoneSurface.Dot, hit.Surface);
        }
        Assert.False(view.TryPick(Pointer(0, 0), false, out _)); // Phone belongs to its own hit test.
        Assert.True(view.TryPick(PanelPointer(panel, 180, 203), false, out var slider));
        Assert.Equal(OpenVrMenuTarget.Opacity, slider.Target);
        state.SetEnvironment(true, true, false);
        view.Render(state, phone, 0.3f, 0.5f, phone, default);
        Assert.False(view.TryPick(Pointer(left - 0.004f, dot.m7), false, out _));
    }

    [Theory]
    [InlineData(8, 169, 0)]
    [InlineData(352, 231, 1)]
    public void SliderCanStartBeyondTheDrawnEndpoints(float x, float y, float expected)
    {
        Assert.True(OpenVrPhoneMenuLayout.TryMap(x / 360, y / 640, false, out var target, out float fraction));
        Assert.Equal(OpenVrMenuTarget.Opacity, target);
        Assert.Equal(expected, fraction);
    }

    [Fact]
    public void CapturedSliderToleratesHorizontalAndVerticalDriftAndReleasesNormally()
    {
        using OpenVrControlTextureTests.OverlayBoundary boundary = new();
        using OpenVrPhoneMenuView view = CreateView(boundary);
        var state = Expanded();
        var phone = OpenVrTransformMath.Identity(); phone.m11 = -1;
        view.Render(state, phone, 0.3f, 0.5f, phone, default);
        var panel = OpenVrPhoneMenuLayout.PanelTransform(OpenVrPhoneMenuLayout.DotTransform(false, phone, 0.3f, 0.5f, phone));
        state.ProcessInput(true, true, OpenVrMenuTarget.Opacity, 0.5f, false, default);
        state.ProcessInput(true, true, OpenVrMenuTarget.Opacity, 0.5f, true, default);
        Assert.True(state.OpacityCaptured);
        state.ProcessInput(true, false, OpenVrMenuTarget.None, 0, true, default);
        Assert.True(state.OpacityCaptured); // Leaving the panel while held does not release capture.
        foreach (var point in new[] { (-40f, 270f, 0), (400f, 145f, 100), (180f, 260f, 50) })
        {
            Assert.True(view.TryPick(PanelPointer(panel, point.Item1, point.Item2), false, out var hit, state.OpacityCaptured));
            Assert.Equal(OpenVrMenuTarget.Opacity, hit.Target);
            var change = state.ProcessInput(true, true, hit.Target, hit.SliderFraction, true, default);
            Assert.Null(change.PhoneCommand); // Never activate the neighboring screen guard.
            Assert.Equal(point.Item3, state.OpacityPercent);
        }
        state.ProcessInput(true, false, OpenVrMenuTarget.None, 0, false, default);
        Assert.False(state.OpacityCaptured);
        Assert.False(view.TryPick(PanelPointer(panel, 400, 145), false, out _, state.OpacityCaptured));
        state.ProcessInput(true, true, OpenVrMenuTarget.Opacity, 0.5f, true, default);
        state.ProcessInput(false, false, OpenVrMenuTarget.None, 0, true, default);
        Assert.False(state.OpacityCaptured);
    }

    [Fact]
    public void GearUsesShellColorWithRoundedCornersAndLargerWhiteIcon()
    {
        byte[] pixels = OpenVrPhoneMenuPixels.Dot();
        Assert.Equal(0, pixels[3]);
        Assert.Equal(new byte[] { 22, 26, 33, 255 }, pixels.AsSpan((32 * 64 + 32) * 4, 4).ToArray());
        Assert.True(pixels[(32 * 64 + 54) * 4] > 220);
        string? directory = Environment.GetEnvironmentVariable("VRPHONE_MENU_SPACING_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        Directory.CreateDirectory(directory);
        Export(Path.Combine(directory, "gear.png"), pixels, 64, 64);
        Export(Path.Combine(directory, "menu.png"), OpenVrPhoneMenuPixels.Panel(new(false, 100, false, 1, false, 0)), 360, 500);
    }

    private static OpenVrPhoneMenuState Expanded()
    {
        OpenVrPhoneMenuState state = new(); state.SetEnvironment(true, false, false);
        state.ProcessInput(true, true, OpenVrMenuTarget.Dot, 0, false, default);
        state.ProcessInput(true, true, OpenVrMenuTarget.Dot, 0, true, default);
        return state;
    }
    private static OpenVrPhoneMenuView CreateView(OpenVrControlTextureTests.OverlayBoundary boundary) =>
        new(boundary.Api, (width, height) => new D3D11ControlTexture(D3D11CreateDevice(DriverType.Warp,
            DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0), width, height));
    private static HmdMatrix34_t Pointer(float x, float y)
    {
        var pointer = OpenVrTransformMath.Identity(); pointer.m3 = x; pointer.m7 = y; return pointer;
    }
    private static HmdMatrix34_t PanelPointer(HmdMatrix34_t panel, float x, float y) =>
        Pointer(panel.m3 + (x / 360 - 0.5f) * OpenVrPhoneMenuLayout.PanelWidth,
            panel.m7 + (0.5f - y / 640) * OpenVrPhoneMenuLayout.PanelWidth / OpenVrPhoneMenuLayout.PanelAspect);
    private static void Export(string path, byte[] pixels, int width, int height)
    {
        using Bitmap bitmap = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                bitmap.SetPixel(x, y, Color.FromArgb(pixels[offset + 3], pixels[offset], pixels[offset + 1], pixels[offset + 2]));
            }
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
