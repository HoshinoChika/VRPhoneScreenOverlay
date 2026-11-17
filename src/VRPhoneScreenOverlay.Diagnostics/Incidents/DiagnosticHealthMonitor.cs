using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>One sampled health writer per process, with no per-log tasks or queue.</summary>
public sealed class DiagnosticHealthMonitor : IDisposable
{
    private readonly RuntimeIncidentSession _session;
    private readonly Func<DiagnosticLogHealth> _log;
    private readonly Func<DiagnosticHardwareObservation>? _hardware;
    private DiagnosticHardwareObservation? _lastHardware;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private string _persistence = "starting";
    private bool _disposed;

    public DiagnosticHealthMonitor(RuntimeIncidentSession session, Func<DiagnosticLogHealth> log,
        Func<DiagnosticHardwareObservation>? hardware = null)
    {
        _session = session;
        _log = log;
        _hardware = hardware;
        _session.SetHealthProvider(Capture);
        _worker = RunAsync(_stop.Token);
    }

    public DiagnosticRuntimeHealth Capture()
    {
        DiagnosticHardwareObservation? hardware = Volatile.Read(ref _lastHardware);
        string hardwareStatus = _hardware is null ? "not-collected" : "sampled";
        if (_hardware is not null)
        {
            try
            {
                DiagnosticHardwareObservation current = _hardware();
                if (current.Snapshot.AndroidModel is not null || current.Snapshot.VideoDecoderBackend is not null)
                {
                    if (hardware is not null && current.Snapshot.AndroidModel is null)
                    {
                        DiagnosticHardwareSummary previous = hardware.Snapshot;
                        current = current with
                        {
                            Status = "partially-last-known",
                            PhoneIsCurrent = false,
                            Snapshot = current.Snapshot with
                            {
                                AndroidManufacturer = previous.AndroidManufacturer,
                                AndroidBrand = previous.AndroidBrand,
                                AndroidModel = previous.AndroidModel,
                                AndroidDeviceCodeName = previous.AndroidDeviceCodeName,
                                AndroidVersion = previous.AndroidVersion,
                                AndroidSdk = previous.AndroidSdk,
                                AndroidCpuAbi = previous.AndroidCpuAbi,
                                AndroidTransport = previous.AndroidTransport,
                                NativeDisplayWidth = previous.NativeDisplayWidth,
                                NativeDisplayHeight = previous.NativeDisplayHeight,
                            },
                        };
                        hardwareStatus = "partially-last-known";
                    }
                    hardware = current;
                    Volatile.Write(ref _lastHardware, current);
                }
                else if (hardware is not null) { hardwareStatus = "last-known"; hardware = hardware with { Status = "last-known", PhoneIsCurrent = false, VideoIsCurrent = false }; }
                else { hardware = current; }
            }
            catch (Exception)
            {
                // Snapshot providers may already be disposed during orderly shutdown.
                hardwareStatus = "collection-failed";
                if (hardware is not null) { hardware = hardware with { Status = "collection-failed", PhoneIsCurrent = false, VideoIsCurrent = false }; }
            }
        }
        return new(DateTimeOffset.UtcNow, _session.StorageStatus, Volatile.Read(ref _persistence), _log())
        { SessionId = _session.SessionId, IncidentStorageFailures = _session.StorageFailures, Hardware = hardware, HardwareCollectionStatus = hardwareStatus };
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _stop.Cancel();
        try
        {
            _worker.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(2));
            SaveAsync(deadline.Token, durable: true).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException)
        { Volatile.Write(ref _persistence, "shutdown-timeout"); }
        finally { _stop.Dispose(); }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
            do { await SaveAsync(token).ConfigureAwait(false); }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async ValueTask SaveAsync(CancellationToken token, bool durable = false)
    {
        string result = await _session.SaveHealthAsync(Capture() with { HealthPersistenceStatus = "ready" }, token, durable).ConfigureAwait(false);
        Volatile.Write(ref _persistence, result);
    }
}
