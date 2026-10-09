using NAudio.CoreAudioApi;

namespace VRPhoneScreenOverlay.Media;

public enum PicoMicrophoneState { Disabled, WaitingForDevice, Starting, Draining, Retrying }

/// <summary>Consumes only the PICO capture endpoint. No samples are copied, queued, stored or played.</summary>
public sealed class PicoMicrophoneKeeper : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<IPicoMicrophoneSession?> _open;
    private CancellationTokenSource? _stop;
    private Task? _worker;
    private volatile bool _enabled;
    private volatile PicoMicrophoneState _state;
    private bool _disposed;
    private long _frames;

    public PicoMicrophoneKeeper() : this(PicoMicrophoneSession.Open) { }
    public PicoMicrophoneKeeper(Func<IPicoMicrophoneSession?> open) => _open = open;
    public PicoMicrophoneState State => _state;
    public long ConsumedFrames => Interlocked.Read(ref _frames);

    public void Configure(bool enabled)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_enabled == enabled) { return; }
            _enabled = enabled;
            _stop?.Cancel();
            if (enabled)
            {
                CancellationTokenSource? previousStop = _stop;
                _stop = new CancellationTokenSource();
                CancellationToken token = _stop.Token;
                Task? previous = _worker;
                _worker = Task.Run(async () =>
                {
                    if (previous is not null) { await previous.ConfigureAwait(false); }
                    previousStop?.Dispose();
                    await RunAsync(token).ConfigureAwait(false);
                });
            }
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _state = PicoMicrophoneState.WaitingForDevice;
                    using IPicoMicrophoneSession? session = _open();
                    if (session is not null)
                    {
                        _state = PicoMicrophoneState.Starting;
                        // One dedicated worker owns COM resources and consumes packets synchronously.
                        // No per-packet tasks or application audio queue are created.
                        await Task.Factory.StartNew(() => session.Drain(frames =>
                        {
                            Interlocked.Add(ref _frames, frames);
                            _state = PicoMicrophoneState.Draining;
                        }, token), token, TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException
                    or InvalidOperationException or UnauthorizedAccessException or TimeoutException)
                {
                    _state = PicoMicrophoneState.Retrying;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { _state = PicoMicrophoneState.Disabled; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            _enabled = false;
            _stop?.Cancel();
            _worker?.GetAwaiter().GetResult();
            _stop?.Dispose();
        }
    }
}

public interface IPicoMicrophoneSession : IDisposable
{
    public void Drain(Action<int> consumed, CancellationToken cancellationToken);
}

internal sealed class PicoMicrophoneSession(string deviceId) : IPicoMicrophoneSession
{
    public static IPicoMicrophoneSession? Open()
    {
        using MMDeviceEnumerator enumerator = new();
        using MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        foreach (MMDevice device in devices)
        {
            using (device)
            {
                if (device.FriendlyName.Contains("PicoStreamingMicrophone", StringComparison.OrdinalIgnoreCase))
                {
                    return new PicoMicrophoneSession(device.ID);
                }
            }
        }
        return null;
    }

    public void Drain(Action<int> consumed, CancellationToken cancellationToken)
    {
        using MMDeviceEnumerator enumerator = new();
        using MMDevice device = enumerator.GetDevice(deviceId);
        using AudioClient client = device.CreateAudioClient();
        using EventWaitHandle ready = new(false, EventResetMode.AutoReset);
        client.Initialize(AudioClientShareMode.Shared, AudioClientStreamFlags.EventCallback,
            200_000, 0, client.MixFormat, Guid.Empty);
        client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
        AudioCaptureClient capture = client.AudioCaptureClient;
        WaitHandle[] signals = [cancellationToken.WaitHandle, ready];
        long lastPacket = Environment.TickCount64;
        client.Start();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(signals, 100) == 0) { break; }
                while (!cancellationToken.IsCancellationRequested && capture.GetNextPacketSize() > 0)
                {
                    _ = capture.GetBuffer(out int frames, out _);
                    capture.ReleaseBuffer(frames);
                    consumed(frames);
                    lastPacket = Environment.TickCount64;
                }
                // Silent WASAPI packets still count. A silent microphone is not treated as stalled.
                if (Environment.TickCount64 - lastPacket > 5_000)
                {
                    throw new TimeoutException("PICO_CAPTURE_STALLED");
                }
            }
        }
        finally
        {
            try { client.Stop(); }
            catch (System.Runtime.InteropServices.COMException) { /* Device removed during teardown. */ }
        }
    }

    public void Dispose() { }
}
