namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private bool _aiRepairControlsHeld;

    private async Task<bool> SetAiRepairControlsAsync(bool busy, CancellationToken token)
    {
        if (IsDisposed || Disposing) { return !busy; }
        bool accepted = false;
        try
        {
            await InvokeAsync(() =>
            {
                if (busy)
                {
                    if (_operationInProgress || _immediateSettingsInProgress || _wirelessOperation is not null || _changingConnectionMethod || _switchingWireless) { return; }
                    _aiRepairControlsHeld = true;
                    SetOperationControls(false);
                }
                else if (_aiRepairControlsHeld)
                {
                    _aiRepairControlsHeld = false;
                    SetOperationControls(true);
                }
                accepted = true;
            }, token);
        }
        // A closing window needs no re-enabling; do not discard an already executed repair's durable outcome.
        catch (InvalidOperationException) when (!busy && (IsDisposed || Disposing)) { return true; }
        return accepted;
    }
    private async Task<bool> PrepareAiReproductionAsync(CancellationToken token)
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            await InvokeAsync(() =>
            {
                _aiReproduction = completion;
                _aboutPage.TroubleshootingPage.SetReproductionPrompt(true);
            }, deadline.Token);
            return await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                await InvokeAsync(() =>
                {
                    _aiReproduction = null;
                    _aboutPage.TroubleshootingPage.SetReproductionPrompt(false);
                }, CancellationToken.None);
            }
        }
    }
}
