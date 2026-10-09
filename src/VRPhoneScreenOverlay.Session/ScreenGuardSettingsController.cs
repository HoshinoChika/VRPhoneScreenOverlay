using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Session;

/// <summary>VR requests and desktop preferences share the atomic settings store.</summary>
public sealed class ScreenGuardSettingsController : IAsyncDisposable, IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly PhoneControlService _control;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;

    public ScreenGuardSettingsController(IAppSettingsService settings, PhoneControlService control)
    {
        _settings = settings;
        _control = control;
        settings.Changed += OnSettingsChanged;
        control.ConfigurePhysicalScreen(settings.Snapshot.Value.AutoTurnOffPhoneScreen);
        _worker = RunAsync();
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs args) =>
        _control.ConfigurePhysicalScreen(args.Snapshot.Value.AutoTurnOffPhoneScreen);

    private async Task RunAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                if (!_control.TakeScreenGuardToggle()) { continue; }
                try
                {
                    using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
                    await _settings.ToggleScreenGuardAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    _control.ReportScreenGuardSaveFailure();
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _worker.ConfigureAwait(false);
            if (_control.TakeScreenGuardToggle())
            {
                using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(3));
                await _settings.ToggleScreenGuardAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            _settings.Changed -= OnSettingsChanged;
            _stop.Dispose();
        }
    }
}
