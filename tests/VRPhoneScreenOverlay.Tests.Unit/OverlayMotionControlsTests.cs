using System.Drawing;
using System.Drawing.Imaging;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OverlayMotionControlsTests
{
    [Theory]
    [InlineData(40, 40, (int)OpenVrMenuTarget.MotionBack)]
    [InlineData(195, 100, (int)OpenVrMenuTarget.MultiplierDown)]
    [InlineData(320, 100, (int)OpenVrMenuTarget.MultiplierUp)]
    [InlineData(195, 165, (int)OpenVrMenuTarget.StrengthDown)]
    [InlineData(320, 165, (int)OpenVrMenuTarget.StrengthUp)]
    [InlineData(195, 235, (int)OpenVrMenuTarget.GravityDown)]
    [InlineData(320, 235, (int)OpenVrMenuTarget.GravityUp)]
    [InlineData(195, 305, (int)OpenVrMenuTarget.FrictionDown)]
    [InlineData(320, 305, (int)OpenVrMenuTarget.FrictionUp)]
    [InlineData(40, 410, (int)OpenVrMenuTarget.ResetModePrevious)]
    [InlineData(320, 410, (int)OpenVrMenuTarget.ResetMode)]
    [InlineData(180, 410, (int)OpenVrMenuTarget.None)]
    [InlineData(100, 440, (int)OpenVrMenuTarget.None)]
    public void MotionPageHitTargetsMatchTheirRenderedRows(int x, int y, int expected)
    {
        Assert.True(OpenVrPhoneMenuLayout.TryMap(x / 360f, y / 640f, true, out var target, out _, motion: true));
        Assert.Equal((OpenVrMenuTarget)expected, target);
    }

    [Theory]
    [InlineData(40, 330, (int)OpenVrMenuTarget.PlayspaceToggle)]
    [InlineData(300, 330, (int)OpenVrMenuTarget.PlayspaceToggle)]
    [InlineData(40, 390, (int)OpenVrMenuTarget.FlingToggle)]
    [InlineData(300, 390, (int)OpenVrMenuTarget.FlingToggle)]
    [InlineData(200, 362, (int)OpenVrMenuTarget.None)]
    [InlineData(200, 465, (int)OpenVrMenuTarget.MotionSettings)]
    [InlineData(10, 465, (int)OpenVrMenuTarget.None)]
    [InlineData(200, 488, (int)OpenVrMenuTarget.None)]
    public void OuterPageGroupsBothSwitchesAndDoesNotExposeMultiplier(int x, int y, int expected)
    {
        Assert.True(OpenVrPhoneMenuLayout.TryMap(x / 360f, y / 640f, false, out var target, out _));
        Assert.Equal((OpenVrMenuTarget)expected, target);
    }

    [Fact]
    public void RuntimeCoordinatorPublishesMotionAndCoalescesPendingSaveWithToggle()
    {
        OpenVrActionUpdateCoordinator coordinator = new();
        var options = new OpenVrPlayspaceMotionOptions(2, 9.8f, 30, true);
        coordinator.SetMotion(true, options);
        Assert.True(coordinator.Read().FlingEnabled);
        Assert.Equal(options, coordinator.Read().MotionOptions);
        coordinator.RequestControls(true, 5, true, options);
        coordinator.RequestControls(true, 5, false);
        var request = coordinator.TakeControls();
        Assert.False(request!.FlingEnabled);
        Assert.Equal(options, request.MotionOptions);
        Assert.Equal(1, coordinator.CoalescedControls);
        Assert.Null(coordinator.TakeControls());
    }

    [Fact]
    public void BackgroundSavesStaySilentUnlessTheyFail()
    {
        OpenVrMenuVisual visual = new(false, 100, true, 5, false, 0, MotionExpanded: true);
        byte[] idle = OpenVrPhoneMenuPixels.Panel(visual);
        Assert.Equal(idle, OpenVrPhoneMenuPixels.Panel(visual with { MotionSaveState = OpenVrMotionSaveState.Pending }));
        Assert.Equal(idle, OpenVrPhoneMenuPixels.Panel(visual with { MotionSaveState = OpenVrMotionSaveState.Saved }));
        Assert.False(idle.AsSpan().SequenceEqual(OpenVrPhoneMenuPixels.Panel(visual with { MotionSaveState = OpenVrMotionSaveState.Failed })));
    }

    [Theory]
    [InlineData("keypad", false, false, false)]
    [InlineData("video", false, false, false)]
    [InlineData("video-busy", false, false, false)]
    [InlineData("video-failed", false, false, false)]
    [InlineData("controls", false, false, false)]
    [InlineData("controls-keypad", false, true, false)]
    [InlineData("motion-off", true, false, false)]
    [InlineData("motion-on", true, false, true)]
    [InlineData("motion-limits", true, true, true)]
    [InlineData("motion-decimal", true, false, true)]
    [InlineData("motion-saving", true, false, true)]
    [InlineData("motion-saved", true, false, true)]
    [InlineData("motion-failed", true, false, true)]
    public void OverlayPixelsRenderEveryControlStateAndOptionallyExportForInspection(string name, bool motion, bool keypad, bool enabled)
    {
        var options = name == "motion-limits" ? new OpenVrPlayspaceMotionOptions(20, 30, 999, true)
            : name == "motion-decimal" ? new OpenVrPlayspaceMotionOptions(19.9f, 29.9f, 998.9f)
            : new OpenVrPlayspaceMotionOptions(1, 9.8f, 0);
        OpenVrMotionSaveState saveState = name switch
        {
            "motion-saving" => OpenVrMotionSaveState.Pending,
            "motion-saved" => OpenVrMotionSaveState.Saved,
            "motion-failed" => OpenVrMotionSaveState.Failed,
            _ => OpenVrMotionSaveState.None,
        };
        byte[] pixels = OpenVrPhoneMenuPixels.Panel(new(false, 100, true, 5, keypad, 0,
            MotionExpanded: motion, FlingEnabled: enabled, MotionOptions: options, MotionSaveState: saveState, VideoExpanded: name.StartsWith("video", StringComparison.Ordinal),
            VideoState: name == "video-busy" ? OpenVrVideoApplyState.Applying : name == "video-failed" ? OpenVrVideoApplyState.Failed : OpenVrVideoApplyState.Idle));
        Assert.Equal(360 * 640 * 4, pixels.Length);
        if (name == "keypad") { OpenVrPhoneMenuPixels.PaintKeypad(pixels, 4); }
        int bottom = name == "keypad" ? 320 : motion || name.StartsWith("video", StringComparison.Ordinal) ? OpenVrPhoneMenuLayout.MotionHeight : keypad ? 564 : 500;
        Assert.NotEqual(0, pixels[((bottom - 10) * 360 + 180) * 4 + 3]);
        Assert.Equal(0, pixels[((bottom + 1) * 360 + 180) * 4 + 3]);
        string? directory = Environment.GetEnvironmentVariable("VRPHONE_MENU_SNAPSHOT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory) || !(name.StartsWith("video", StringComparison.Ordinal) || name.StartsWith("controls", StringComparison.Ordinal) || name.StartsWith("motion", StringComparison.Ordinal))) { return; }
        Directory.CreateDirectory(directory);
        using Bitmap bitmap = new(360, bottom);
        for (int y = 0; y < bottom; y++)
        {
            for (int x = 0; x < 360; x++)
            {
                int offset = (y * 360 + x) * 4;
                bitmap.SetPixel(x, y, Color.FromArgb(pixels[offset + 3], pixels[offset], pixels[offset + 1], pixels[offset + 2]));
            }
        }
        bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
    }
}
