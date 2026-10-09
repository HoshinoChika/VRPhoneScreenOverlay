using System.Drawing;
using System.Windows.Forms;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class VerifiedControllerCatalogTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void RefreshPreservesTheChosenBrandAndEveryRetailLabelFits(float scale)
    {
        RunOnUiThread(() =>
        {
            using MainShellView shell = new();
            using ControllerBindingsView page = new();
            shell.PageHost.Controls.Add(page);
            page.Location = UiLayoutMetrics.PageLocation;
            page.ControllerSelector.SetNodes(OpenVrBindingGuide.Choices.Select(choice => new FixedChoiceNode<string>(choice.Key, choice.Label)), "pico_controller");
            using UiScaleLayout layout = new(shell);
            layout.Apply(scale);
            shell.CreateControl();
            foreach (ControllerBindingChoice choice in OpenVrBindingGuide.Choices)
            {
                Assert.True(page.ControllerSelector.SelectValue(choice.Key));
                page.SetGuide(new(choice.ControllerType, "正在读取", "", []));
                page.SetGuide(new(choice.ControllerType, "已保存绑定", "", []));
                Assert.True(page.ControllerSelector.TryGetSelectedValue(out string key));
                Assert.Equal(choice.Key, key);
                Label label = page.ControllerSelector.Controls.OfType<Label>().Single();
                Assert.Equal(choice.Label, label.Text);
                int width = TextRenderer.MeasureText(label.Text, label.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
                Assert.True(width <= label.ClientSize.Width - label.Padding.Horizontal, choice.Label);
                Assert.True(page.ClientRectangle.Contains(page.ControllerSelector.Bounds));
                Assert.False(page.ControllerSelector.Bounds.IntersectsWith(page.RefreshButton.Bounds));
            }
        });
    }

    [Theory]
    [InlineData("pico_controller_ice")]
    [InlineData("custom_driver")]
    [InlineData("holographic_controller")]
    [InlineData("oculus_touch")]
    public void UnlistedSavedProfilesUseACurrentBindingEntryWithoutInventingARetailModel(string type)
    {
        RunOnUiThread(() =>
        {
            using ControllerBindingsView page = new();
            page.SetGuide(new(type, "已保存绑定", "", []));
            Assert.True(page.ControllerSelector.TryGetSelectedValue(out string key));
            Assert.Equal(type, OpenVrBindingGuide.ResolveControllerType(key));
            Assert.Equal("当前绑定", page.ControllerSelector.Controls.OfType<Label>().Single().Text);
            page.SetGuide(new(type, "已保存绑定", "", []));
            Assert.True(page.ControllerSelector.TryGetSelectedValue(out key));
            Assert.Equal(type, OpenVrBindingGuide.ResolveControllerType(key));
        });
    }

    private static void RunOnUiThread(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() => { try { test(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }
}
