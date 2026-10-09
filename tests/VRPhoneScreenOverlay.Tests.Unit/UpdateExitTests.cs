using System.Windows.Forms;
using VRPhoneScreenOverlay.App;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class UpdateExitTests
{
    [Fact]
    public void ApplicationExitUsesPreparationWithoutShowingAUserClosePrompt()
    {
        RunOnUiThread(() =>
        {
            using ProbeForm form = new();
            bool prepared = false;
            form.Preparation = async () => { await Task.Delay(20); prepared = true; };
            form.Shown += (_, _) => Assert.True(form.RequestApplicationExit());
            RunWindow(form);
            Assert.True(prepared);
            Assert.Equal(0, form.ConfirmationCount);
        });
    }

    [Fact]
    public void ExitKeepsTheMessageLoopAliveUntilUiRollbackCompletes()
    {
        RunOnUiThread(() =>
        {
            using ProbeForm form = new();
            UiOperationLifetime lifetime = new();
            bool rollbackCompleted = false;
            form.Preparation = lifetime.StopAsync;
            Task? operation = null;
            async Task RunOperationAsync()
            {
                using UiOperationLifetime.Lease lease = lifetime.TryEnter(CancellationToken.None)!;
                try { await Task.Delay(Timeout.InfiniteTimeSpan, lease.Token); }
                catch (OperationCanceledException)
                {
                    // This continuation needs the real WinForms synchronization context.
                    await Task.Delay(30);
                    Assert.False(form.IsDisposed);
                    rollbackCompleted = true;
                }
            }
            form.Shown += (_, _) =>
            {
                operation = RunOperationAsync();
                form.ExitExplicitly();
                Assert.False(form.IsDisposed);
                Assert.False(rollbackCompleted);
            };
            RunWindow(form);
            Assert.True(rollbackCompleted);
            Assert.True(operation!.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public void OrdinaryClosePromptsButSuccessfulUpdateClosesThroughTheRealWindowMessageLoop()
    {
        RunOnUiThread(() =>
        {
            using ProbeForm form = new();
            bool closed = false;
            bool maintenanceStarted = false;
            int promptsAfterUserClose = 0;
            form.FormClosed += (_, _) => closed = true;
            form.Shown += (_, _) =>
            {
                form.Close(); // Reproduces the former BeginInvoke(Close) path.
                promptsAfterUserClose = form.ConfirmationCount;
                Assert.False(closed);
                form.StartUpdate(() => maintenanceStarted = true);
            };
            RunWindow(form);
            Assert.True(maintenanceStarted);
            Assert.True(closed);
            Assert.Equal(1, promptsAfterUserClose);
            Assert.Equal(1, form.ConfirmationCount); // Update did not prompt again.
        });
    }

    [Fact]
    public void MaintenanceLaunchFailureDoesNotAuthorizeExitOrLoseTheClosePrompt()
    {
        RunOnUiThread(() =>
        {
            using ProbeForm form = new();
            bool closed = false;
            form.FormClosed += (_, _) => closed = true;
            form.Shown += (_, _) =>
            {
                Assert.Throws<InvalidOperationException>(() => form.StartUpdate(
                    () => throw new InvalidOperationException("synthetic launch failure")));
                form.Close();
                Assert.False(closed);
                Assert.Equal(1, form.ConfirmationCount);
                form.ExitExplicitly();
            };
            RunWindow(form);
            Assert.True(closed);
        });
    }

    private static void RunWindow(ProbeForm form)
    {
        using System.Windows.Forms.Timer timeout = new() { Interval = 3000 };
        bool timedOut = false;
        timeout.Tick += (_, _) => { timedOut = true; form.ExitExplicitly(); };
        timeout.Start();
        Application.Run(form);
        Assert.False(timedOut, "The application remained open while maintenance waited for its exit.");
    }

    [Fact]
    public void NormalMinimizeKeepsTheTaskbarEntryAndDoesNotShowTheTray()
    {
        RunOnUiThread(() =>
        {
            using ProbeForm form = new() { ShowInTaskbar = true };
            FakeTray tray = new();
            form.AttachTray(tray);
            form.Shown += (_, _) =>
            {
                form.WindowState = FormWindowState.Minimized;
                Assert.Equal(FormWindowState.Minimized, form.WindowState);
                Assert.True(form.Visible);
                Assert.True(form.ShowInTaskbar);
                Assert.False(tray.Visible);
                form.WindowState = FormWindowState.Normal;
                Assert.True(form.Visible);
                Assert.True(form.ShowInTaskbar);
                Assert.False(tray.Visible);
                form.ExitExplicitly();
            };
            RunWindow(form);
            Assert.True(tray.Disposed);
            Assert.Equal(0, form.ConfirmationCount);
        });
    }

    [Fact]
    public void TrayMinimizeHidesTaskbarEntryAndRestoreKeepsTheSameWindowAlive()
    {
        RunOnUiThread(() =>
        {
            using ProbeForm form = new();
            FakeTray tray = new();
            form.AttachTray(tray);
            form.Shown += (_, _) =>
            {
                form.HideToTray();
                Assert.True(tray.Visible);
                Assert.False(form.Visible);
                Assert.False(form.ShowInTaskbar);
                Assert.False(form.IsDisposed);
                tray.Restore();
                Assert.True(form.Visible);
                Assert.True(form.ShowInTaskbar);
                Assert.Equal(FormWindowState.Normal, form.WindowState);
                Assert.False(tray.Visible);
                tray.Exit();
            };
            RunWindow(form);
            Assert.True(tray.Disposed);
            Assert.Equal(0, form.ConfirmationCount);
        });
    }

    [Fact]
    public void SuccessfulUpdateAlsoExitsWhileTheMainWindowIsInTheTray()
    {
        RunOnUiThread(() =>
        {
            using ProbeForm form = new();
            FakeTray tray = new();
            form.AttachTray(tray);
            bool launched = false;
            form.Shown += (_, _) =>
            {
                form.HideToTray();
                form.StartUpdate(() => launched = true);
            };
            RunWindow(form);
            Assert.True(launched);
            Assert.True(tray.Disposed);
            Assert.False(tray.Visible);
            Assert.Equal(0, form.ConfirmationCount);
        });
    }

    private static void RunOnUiThread(Action test)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); test(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "UI exit regression timed out.");
        Assert.Null(failure);
    }

    private sealed class ProbeForm : ExitConfirmationForm
    {
        public int ConfirmationCount { get; private set; }
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Func<Task>? Preparation { get; set; }
        protected override Task PrepareExitAsync() => Preparation?.Invoke() ?? Task.CompletedTask;
        public ProbeForm() { ShowInTaskbar = false; Opacity = 0; }
        protected override void ShowExitConfirmation() => ConfirmationCount++;
        public void StartUpdate(Action launch) => CloseAfterMaintenanceStarted(launch);
        public void ExitExplicitly() => ConfirmExit();
        public bool RequestApplicationExit()
        {
            FormClosingEventArgs args = new(CloseReason.ApplicationExitCall, false);
            OnFormClosing(args);
            return args.Cancel;
        }
        public void AttachTray(IWindowTray tray) => ConfigureTray(tray);
        public void HideToTray() => MinimizeToTray();
    }

    private sealed class FakeTray : IWindowTray
    {
        public bool Visible { get; set; }
        public bool Disposed { get; private set; }
        public event EventHandler? RestoreRequested;
        public event EventHandler? ExitRequested;
        public void Restore() => RestoreRequested?.Invoke(this, EventArgs.Empty);
        public void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);
        public void Dispose() { Visible = false; Disposed = true; }
    }
}
