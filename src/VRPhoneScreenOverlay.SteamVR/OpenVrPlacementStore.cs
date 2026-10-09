using System.Text.Json;
using System.Threading.Channels;

namespace VRPhoneScreenOverlay.SteamVR;

internal sealed record OpenVrSavedPlacement(float Scale, int SchemaVersion = 2)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => SchemaVersion == 2 && float.IsFinite(Scale) && Scale is >= 0.2f and <= 2.5f;
}

// One owned writer per phone session, capacity 1, latest placement wins. Only
// grab-release/close enqueue; disk I/O never runs on the pointer polling thread.
internal sealed class OpenVrPlacementStore : IDisposable
{
    private readonly string _path;
    private readonly string _temporaryPath;
    private readonly Channel<OpenVrSavedPlacement> _pending;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private long _dropped;
    private long _written;
    private long _failures;
    private int _disposed;

    public OpenVrPlacementStore(string path)
    {
        _path = path;
        _temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        _pending = Channel.CreateBounded<OpenVrSavedPlacement>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        }, _ => Interlocked.Increment(ref _dropped));
        _worker = WritePendingAsync(_stop.Token);
    }

    public long Dropped => Interlocked.Read(ref _dropped);
    public long Written => Interlocked.Read(ref _written);
    public long Failures => Interlocked.Read(ref _failures);

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VRPhoneScreenOverlay", "phone-overlay-placement.json");

    public static async Task<OpenVrSavedPlacement?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096)
            {
                return null;
            }

            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            OpenVrSavedPlacement? value = JsonSerializer.Deserialize<OpenVrSavedPlacement>(json);
            // Version 1 also stored a head-relative pose. Migrate only its scale;
            // unknown legacy position fields are deliberately ignored.
            if (value?.SchemaVersion == 1) { value = value with { SchemaVersion = 2 }; }
            return value?.IsValid == true ? value : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or OperationCanceledException)
        {
            return null;
        }
    }

    public void Queue(OpenVrSavedPlacement value)
    {
        if (Volatile.Read(ref _disposed) == 0 && value.IsValid)
        {
            _pending.Writer.TryWrite(value);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _pending.Writer.TryComplete();
        if (!_worker.Wait(TimeSpan.FromSeconds(2)))
        {
            _stop.Cancel();
            _ = _worker.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                _stop, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        else
        {
            _stop.Dispose();
        }
    }

    private async Task WritePendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (OpenVrSavedPlacement value in _pending.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    await File.WriteAllTextAsync(_temporaryPath, JsonSerializer.Serialize(value), cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(_temporaryPath, _path, overwrite: true);
                    Interlocked.Increment(ref _written);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Interlocked.Increment(ref _failures);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                File.Delete(_temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref _failures);
            }
        }
    }
}
