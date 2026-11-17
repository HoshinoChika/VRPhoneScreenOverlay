namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    private TaskCompletionSource<bool?>? _memoryConsent;

    private void InitializeMemoryConsent()
    {
        _aboutPage.ConfirmMemoryUploadButton.Click += (_, _) => _memoryConsent?.TrySetResult(_aboutPage.IncludeMemory);
        _aboutPage.CancelMemoryUploadButton.Click += (_, _) => _memoryConsent?.TrySetResult(null);
    }

    private async Task<bool?> ConfirmMemoryUploadAsync()
    {
        if (_memoryConsent is not null) { return null; }
        TaskCompletionSource<bool?> choice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _memoryConsent = choice;
        using CancellationTokenRegistration registration = _formLifetime.Token.Register(() => choice.TrySetResult(null));
        try
        {
            _aboutPage.ShowMemoryConsent();
            return await choice.Task;
        }
        finally
        {
            _memoryConsent = null;
            if (!IsDisposed && !Disposing) { _aboutPage.HideMemoryConsent(); }
        }
    }
}
