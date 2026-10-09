using System.Drawing;
using System.Windows.Forms;
using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.App.Views;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PublicReleaseUpdateTests
{
    private static readonly float[] _scales = [1f, 1.5f, 2f];
    [Fact]
    public async Task StartupHasOneDelayedOpportunityAndManualCheckCanSkipIt()
    {
        StartupUpdateGate gate = new(TimeSpan.Zero);
        Assert.True(await gate.WaitOnceAsync(CancellationToken.None));
        Assert.False(await gate.WaitOnceAsync(CancellationToken.None));
        gate = new(TimeSpan.FromMilliseconds(20));
        Task<bool> pending = gate.WaitOnceAsync(CancellationToken.None);
        gate.Skip();
        Assert.False(await pending);
        Assert.False(await gate.WaitOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ClosingWindowCancelsDelayedCheckInsteadOfRetrying()
    {
        StartupUpdateGate gate = new(TimeSpan.FromSeconds(30));
        using CancellationTokenSource stop = new();
        Task<bool> pending = gate.WaitOnceAsync(stop.Token);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(await gate.WaitOnceAsync(CancellationToken.None));
    }

    [Fact]
    public void ClientEndpointsComeFromTheSameConfiguration()
    {
        ClientServiceConfiguration configuration = TestServiceConfiguration.Value;
        Assert.Equal(configuration.UpdateManifestUri, UpdateServiceOptions.FromConfiguration(configuration).ManifestUri);
        Assert.Equal(configuration.DiagnosticsInitUri, DiagnosticsServiceOptions.FromConfiguration(configuration).InitUri);
        Assert.Null(UpdateServiceOptions.FromConfiguration(ClientServiceConfiguration.Disabled).ManifestUri);
    }

    [Fact]
    public void UpdatePromptShowsNotesThenRestoresExitActionsWithoutLosingTheLayer()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using MainShellView shell = new();
                var prompt = shell.CloseConfirmation;
                prompt.ConfigureUpdate("0.2.6-beta.10", "新服务地址\r\n启动更新提醒\r\n诊断隐私检查");
                prompt.Visible = true;
                Assert.True(prompt.IsUpdate);
                Assert.Contains("启动更新提醒", prompt.UpdateNotes);
                Assert.Equal("更新", prompt.MinimizeButton.Text);
                Assert.Equal("暂不更新", prompt.ExitButton.Text);
                Assert.False(shell.PageHost.Enabled);
                Assert.False(prompt.CancelButton.Visible);
                string? output = Environment.GetEnvironmentVariable("VRPHONE_UPDATE_PROMPT_SNAPSHOT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    using Form host = new() { ClientSize = shell.Size };
                    host.Controls.Add(shell);
                    host.Show();
                    Application.DoEvents();
                    using Bitmap image = new(shell.Width, shell.Height);
                    prompt.Visible = false;
                    shell.DrawToBitmap(image, shell.ClientRectangle);
                    prompt.Visible = true;
                    using Bitmap layer = new(prompt.Width, prompt.Height);
                    prompt.DrawToBitmap(layer, prompt.ClientRectangle);
                    using (Graphics graphics = Graphics.FromImage(image)) { graphics.DrawImageUnscaled(layer, prompt.Location); }
                    image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                    host.Controls.Remove(shell);
                }
                using (UiScaleLayout layout = new(shell))
                {
                    foreach (float scale in _scales)
                    {
                        layout.Apply(scale);
                        prompt.ConfigureUpdate("0.2.6-beta.10", "更新内容");
                        Assert.Equal((int)MathF.Round(280 * scale), prompt.MinimizeButton.Top);
                        prompt.ConfigureClose();
                        Assert.Equal((int)MathF.Round(142 * scale), prompt.MinimizeButton.Top);
                    }
                }
                prompt.ConfigureClose();
                Assert.False(prompt.IsUpdate);
                Assert.Equal("最小化到托盘", prompt.MinimizeButton.Text);
                Assert.Equal("退出程序", prompt.ExitButton.Text);
                prompt.Visible = false;
                Assert.True(shell.PageHost.Enabled);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
