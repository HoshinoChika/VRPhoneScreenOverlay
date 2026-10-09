using System.Drawing;
using System.Windows.Forms;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ContextHelpTests
{
    [Fact]
    public void ConnectionManagementHelpUsesTheRequestedInstruction()
        => Assert.Equal("选择一个连接方式，然后根据步骤操作手机即可。", UiHelpContent.Wireless);
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void EveryHelpCardFitsInsideTheMainWindowAndHasReadableContent(float scale)
    {
        RunOnUiThread(() =>
        {
            foreach (Func<Control> create in new Func<Control>[]
            {
                () => new SettingsPageView(), () => new VideoPageView(), () => new PlayspaceMotionView(),
                () => new HomePageView(), () => new AboutPageView(), () => new WirelessConnectionView(),
                () => new ControllerBindingsView(),
            })
            {
                using MainShellView shell = new();
                using Control page = create();
                shell.PageHost.Controls.Add(page);
                page.Location = UiLayoutMetrics.PageLocation;
                using UiScaleLayout layout = new(shell);
                layout.Apply(scale);
                shell.CreateControl();
                page.CreateControl();
                ContextHelpIcon[] icons = Descendants(page).OfType<ContextHelpIcon>().ToArray();
                Assert.NotEmpty(icons);
                foreach (ContextHelpIcon icon in icons.Where(icon => icon.Visible))
                {
                    Assert.False(string.IsNullOrWhiteSpace(icon.AccessibleDescription));
                    Assert.NotNull(icon.TargetLabel);
                    Assert.Equal(ContentAlignment.MiddleLeft, icon.TargetLabel.TextAlign);
                    Assert.InRange(Math.Abs((icon.Top * 2 + icon.Height) -
                        (icon.TargetLabel.Top * 2 + icon.TargetLabel.Height)), 0, 1);
                    Assert.InRange(icon.Left - icon.TargetLabel.Right, 0, (int)Math.Ceiling(4 * scale));
                    Assert.DoesNotContain('\n', icon.HelpText);
                    Assert.True(icon.HelpText.Length <= 100);
                    foreach (Control sibling in icon.Parent!.Controls)
                    {
                        if (sibling != icon && sibling.Visible)
                        { Assert.False(icon.Bounds.IntersectsWith(sibling.Bounds), $"Help overlaps {sibling.Name}"); }
                    }
                    icon.ShowHelp();
                    ContextHelpPopup popup = shell.ContextHelp;
                    Assert.True(popup.Visible);
                    Assert.Same(shell, popup.Parent);
                    Assert.True(shell.ClientRectangle.Contains(popup.Bounds));
                    Assert.False(popup.Bounds.IntersectsWith(shell.RectangleToClient(icon.RectangleToScreen(icon.ClientRectangle))));
                    Size text = TextRenderer.MeasureText(icon.HelpText, popup.Font,
                        new Size(popup.Width - popup.Padding.Horizontal, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    Assert.True(text.Height <= popup.Height - popup.Padding.Vertical, icon.AccessibleName);
                    icon.HideHelp();
                    Assert.False(popup.Visible);
                }
            }
        });
    }

    [Fact]
    public void SwitchingFromCodeToScanDoesNotOpenHelpWhenWindowsMovesFocus()
    {
        RunOnUiThread(() =>
        {
            using Form form = new() { ShowInTaskbar = false, Opacity = 0, ClientSize = new Size(758, 502) };
            using MainShellView shell = new();
            using WirelessConnectionView page = new();
            form.Controls.Add(shell);
            shell.PageHost.Controls.Add(page);
            page.Location = UiLayoutMetrics.PageLocation;
            form.Show();
            Application.DoEvents();
            shell.ContextHelp.HideHelp();
            page.MethodChanged += (_, _) =>
            {
                if (page.Method == WirelessPairingMethod.Scan)
                {
                    // Matches QR startup's temporary control disable, without invoking ADB.
                    page.SetBusy(true);
                    page.SetQrWaiting();
                }
            };
            RoundConnectionChoice code = Descendants(page).OfType<RoundConnectionChoice>().Single(choice => Equals(choice.Tag, WirelessPairingMethod.Code));
            Assert.True(code.Focus());
            code.Checked = true;
            RoundConnectionChoice scan = Descendants(page).OfType<RoundConnectionChoice>().Single(choice => Equals(choice.Tag, WirelessPairingMethod.Scan));
            // Focusing a radio selects it; QR startup temporarily disables its parent,
            // so Windows can move focus before Focus() returns.
            _ = scan.Focus();
            scan.Checked = true;
            Assert.Equal(WirelessPairingMethod.Scan, page.Method);
            Application.DoEvents();
            Assert.False(shell.ContextHelp.Visible, "Panel focus transfer must not open a help card.");
            ContextHelpIcon icon = Descendants(page).OfType<ContextHelpIcon>().Single(value => value.Name == "titleHelp");
            Assert.True(icon.Focus());
            Assert.False(shell.ContextHelp.Visible);
            typeof(ContextHelpIcon).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(icon, [new KeyEventArgs(Keys.Enter)]);
            Assert.True(shell.ContextHelp.Visible); // Explicit keyboard request remains available.
            typeof(ContextHelpIcon).GetMethod("OnMouseLeave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(icon, [EventArgs.Empty]);
            Assert.False(shell.ContextHelp.Visible, "Focus must not keep a help card pinned after mouse leave.");
        });
    }

    [Fact]
    public void AboutProductCardIncludesSourceAndContactWhileUpdateStatusStaysInUpdateCard()
    {
        RunOnUiThread(() =>
        {
            using AboutPageView page = new();
            page.SetProductDetails("版本 0.2.6-beta.10");
            Control card = page.Controls.Find("productCard", true).Single();
            Assert.Empty(Descendants(card).OfType<ContextHelpIcon>());
            Label[] labels = Descendants(card).OfType<Label>().ToArray();
            Assert.Contains(labels, label => label.Text == "开源地址：");
            Assert.Contains(labels, label => label is LinkLabel && label.Text == "VRPhoneScreenOverlay");
            Assert.Contains(labels, label => label.Text == "联系作者（打开抖音扫一扫）");
            Assert.Equal("updateCard", page.OperationStatus.Parent!.Name);
            Assert.Equal("diagnosticsCard", page.DiagnosticsStatus.Parent!.Name);
            Assert.NotSame(page.OperationStatus, page.DiagnosticsStatus);
            Assert.Contains(labels, label => label.Text == "VRPhoneScreen Overlay");
            Assert.Contains(labels, label => label.Text == "版本 0.2.6-beta.10");
        });
    }

    [Fact]
    public void MouseHoverShowsHelpAndLeavingTheIconHidesIt()
    {
        RunOnUiThread(() =>
        {
            using MainShellView shell = new();
            using SettingsPageView page = new();
            shell.PageHost.Controls.Add(page);
            shell.CreateControl();
            ContextHelpIcon icon = Descendants(page).OfType<ContextHelpIcon>().First();
            icon.CreateControl();
            typeof(ContextHelpIcon).GetMethod("OnMouseEnter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(icon, [EventArgs.Empty]);
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (!shell.ContextHelp.Visible && DateTime.UtcNow < deadline)
            { Application.DoEvents(); Thread.Sleep(10); }
            Assert.True(shell.ContextHelp.Visible);
            typeof(ContextHelpIcon).GetMethod("OnMouseLeave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(icon, [EventArgs.Empty]);
            Assert.False(shell.ContextHelp.Visible);
        });
    }

    [Fact]
    public void LeavingThePageOrOpeningAConfirmationDismissesHelp()
    {
        RunOnUiThread(() =>
        {
            using MainShellView shell = new();
            using SettingsPageView page = new();
            shell.PageHost.Controls.Add(page);
            shell.CreateControl();
            ContextHelpIcon icon = Descendants(page).OfType<ContextHelpIcon>().First();
            icon.ShowHelp();
            Assert.True(shell.ContextHelp.Visible);
            page.Visible = false;
            Assert.False(shell.ContextHelp.Visible);
            page.Visible = true;
            icon.ShowHelp();
            shell.CloseConfirmation.Visible = true;
            Assert.False(shell.ContextHelp.Visible);
            icon.ShowHelp();
            Assert.False(shell.ContextHelp.Visible);
        });
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) { yield return nested; }
        }
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
