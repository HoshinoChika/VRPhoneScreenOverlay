using System.Windows.Forms;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class BindingPresentationStabilityTests
{
    [Fact]
    public async Task LoadingFailureAndChangedModelRetainTheExistingLabelsAndScrollPosition()
    {
        ControllerBindingGuide original = await OpenVrBindingGuide.ReadDefaultAsync("pico_controller", OpenVrControllerHand.Right, CancellationToken.None);
        ControllerBindingGuide changed = await OpenVrBindingGuide.ReadDefaultAsync("vive_controller", OpenVrControllerHand.Right, CancellationToken.None);
        using ControllerBindingsView page = new();
        ControllerBindingInstructions instructions = (ControllerBindingInstructions)page.Controls.Find("instructions", true).Single();
        instructions.Size = new(596, 160);
        page.SetGuide(original);
        instructions.CreateControl();
        instructions.PerformLayout();
        instructions.AutoScrollPosition = new(0, 60);
        int scroll = instructions.AutoScrollPosition.Y;
        Label[] labels = Labels(instructions).ToArray();
        page.BeginGuideRead();
        page.FailGuideRead();
        Assert.Equal(labels, Labels(instructions));
        Assert.Equal(scroll, instructions.AutoScrollPosition.Y);
        page.SetGuide(changed);
        Assert.Equal(labels, Labels(instructions));
        Assert.All(labels, value => Assert.False(value.IsDisposed));
        Assert.Equal(scroll, instructions.AutoScrollPosition.Y);
    }

    [Fact]
    public void RepeatedLayoutsDoNotRearrangeUnchangedInstructionRows()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using ControllerBindingInstructions instructions = new() { Size = new(596, 160) };
                instructions.SetGuide(OpenVrBindingGuide.ReadDefaultAsync("pico_controller", OpenVrControllerHand.Right, CancellationToken.None).AsTask().GetAwaiter().GetResult());
                instructions.CreateControl();
                instructions.PerformLayout(); // settle native scrollbars after handle creation
                Assert.True(instructions.AutoScroll);
                int rowLayouts = 0;
                foreach (Control group in instructions.Controls)
                { foreach (TableLayoutPanel rows in group.Controls.OfType<TableLayoutPanel>()) { rows.Layout += (_, _) => rowLayouts++; } }
                for (int repeat = 0; repeat < 20; repeat++) { instructions.PerformLayout(); }
                Assert.Equal(0, rowLayouts);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    [Fact]
    public void ResetKeepsInstructionsAndNoticesStableThenAppliesTheLatestGuideOnce()
    {
        using ControllerBindingsView page = new();
        ControllerBindingGuide original = new("pico_controller", "原配置", "", [new("返回", "/user/hand/right", "/a", "button", "click")]);
        page.SetGuide(original);
        ControllerBindingInstructions instructions = (ControllerBindingInstructions)page.Controls.Find("instructions", true).Single();
        Label[] labels = Labels(instructions).ToArray();
        page.BeginRestoreDefaults();
        page.OperationStatus.Text = "正在恢复默认绑定…";
        ControllerBindingGuide latest = original with { Source = "默认绑定", Entries = [new("返回", "/user/hand/right", "/b", "button", "click")] };
        for (int repeat = 0; repeat < 20; repeat++)
        {
            page.UpdateBindingNotice(OpenVrBindingHealthState.Loading, true, "加载中");
            page.SetGuide(latest);
            page.UpdateBindingNotice(OpenVrBindingHealthState.Ready, false, "");
        }
        Assert.False(page.BindingNotice.Visible);
        Assert.Equal(labels, Labels(instructions));
        Assert.All(labels, label => Assert.False(label.IsDisposed));
        Assert.Equal("正在恢复默认绑定…", page.OperationStatus.Text);
        page.OperationStatus.Text = "已恢复默认绑定";
        page.EndRestoreDefaults();
        Assert.Equal("已恢复默认绑定", page.OperationStatus.Text);
        Assert.Equal(labels, Labels(instructions));
        Assert.All(labels, label => Assert.False(label.IsDisposed));
        Assert.Contains(Labels(instructions), label => label.Text.Contains("B 键", StringComparison.Ordinal));
        Label[] refreshed = Labels(instructions).ToArray();
        page.SetGuide(latest with { Entries = latest.Entries.ToArray() });
        Assert.Equal(refreshed, Labels(instructions));
    }

    private static IEnumerable<Label> Labels(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Label label) { yield return label; }
            foreach (Label nested in Labels(child)) { yield return nested; }
        }
    }
}
