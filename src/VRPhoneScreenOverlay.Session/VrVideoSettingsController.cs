using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public sealed class VrVideoSettingsController : IAsyncDisposable, IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;

    public VrVideoSettingsController(OpenVrVideoSettingsChannel channel, SettingsApplicationService application,
        IAppSettingsService settings, IAndroidConnectionService phone)
    {
        channel.ConfigureChoices(AppSettingsPolicy.VideoBitratesMbps, AppSettingsPolicy.VideoMaximumFrameRates);
        _worker = RunAsync(channel, application, settings, phone, _stop.Token);
    }

    private static async Task RunAsync(OpenVrVideoSettingsChannel channel, SettingsApplicationService application,
        IAppSettingsService settings, IAndroidConnectionService phone, CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
        (int Width, int Height) previousDimensions = default;
        IReadOnlyList<int> resolutions = [100];
        AppSettings? previousSettings = null;
        try
        {
            do
            {
                AppSettings current = settings.Snapshot.Value;
                AndroidDeviceDetails? device = phone.Snapshot.SelectedDevice;
                (int Width, int Height) dimensions = (device?.NativeDisplayWidth ?? 0, device?.NativeDisplayHeight ?? 0);
                if (dimensions != previousDimensions || current != previousSettings)
                {
                    if (dimensions != previousDimensions)
                    {
                        resolutions = dimensions is { Width: > 0, Height: > 0 }
                            ? VideoResolutionProfiles.Create(dimensions.Width, dimensions.Height)
                                .Select(value => value.Percent).Order().ToArray() : [100];
                        previousDimensions = dimensions;
                    }
                    Publish(current);
                    previousSettings = current;
                }
                if (channel.Take() is { } request)
                {
                    bool success = false;
                    try
                    {
                        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        deadline.CancelAfter(TimeSpan.FromSeconds(45));
                        await application.ApplyAsync(settings.Snapshot.Value with
                        {
                            VideoResolutionPercent = resolutions.Contains(request.Resolution) ? request.Resolution : 100,
                            VideoBitrateMbps = request.Bitrate,
                            VideoMaximumFramesPerSecond = request.FrameRate,
                        }, deadline.Token).ConfigureAwait(false);
                        success = true;
                    }
                    catch (SettingsApplyException) { }
                    catch (OperationCanceledException) { }
                    finally { Publish(settings.Snapshot.Value); channel.Complete(success); }
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }

        void Publish(AppSettings value) => channel.Publish(new(value.VideoResolutionPercent,
            value.VideoBitrateMbps, value.VideoMaximumFramesPerSecond), resolutions);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _worker.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
