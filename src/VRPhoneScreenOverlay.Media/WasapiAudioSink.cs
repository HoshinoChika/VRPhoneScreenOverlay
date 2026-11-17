using System.Runtime.InteropServices;
using NAudio.Wave;

namespace VRPhoneScreenOverlay.Media;

internal sealed class WasapiAudioSink(
    BufferedWaveProvider buffer,
    WasapiPlayer player, ObservedAudioProvider observed, AudioClockRegulator? regulator = null) : IAudioSink
{
    private readonly BufferedWaveProvider _buffer = buffer;
    private readonly WasapiPlayer _player = player;
    private bool _disposed;
    private long _regulatorResets;
    private long _nextClockObservation;
    private double? _position, _latency;
    private DateTimeOffset? _clockSampledAt;

    public VRPhoneScreenOverlay.Contracts.AudioPlaybackDiagnostic? PlaybackDiagnostic
    {
        get
        {
            if (_disposed) { _position = null; _latency = null; _clockSampledAt = null; return SnapshotPlayback(); }
            long now = Environment.TickCount64;
            if (now < _nextClockObservation) { return SnapshotPlayback(); }
            _nextClockObservation = now + 1000;
            double? position = null, latency = null;
            try
            {
                if (!_disposed && _player.PlaybackState == PlaybackState.Playing)
                {
                    double measuredPosition = _player.GetPosition() * 1000d / _player.OutputWaveFormat.AverageBytesPerSecond;
                    double measuredLatency = _player.CurrentLatency.TotalMilliseconds;
                    if (double.IsFinite(measuredPosition) && measuredPosition >= 0) { position = measuredPosition; }
                    if (double.IsFinite(measuredLatency) && measuredLatency >= 0) { latency = measuredLatency; }
                }
            }
            catch (Exception error) when (error is COMException or InvalidOperationException) { }
            _position = position; _latency = latency; _clockSampledAt = position.HasValue ? DateTimeOffset.UtcNow : null;
            return SnapshotPlayback();
        }
    }

    private VRPhoneScreenOverlay.Contracts.AudioPlaybackDiagnostic SnapshotPlayback() => observed.Snapshot(_position, _latency) with
    {
        DeviceClockSampledAt = _clockSampledAt,
        TimestampDiscontinuities = regulator?.TimestampDiscontinuities ?? 0,
        CorrectionRatio = regulator?.CorrectionRatio ?? 0,
        AdjustedFrames = regulator?.AdjustedFrames ?? 0,
        ResamplerPendingMilliseconds = regulator?.PendingMilliseconds ?? 0,
        RegulatorHeadroomResets = _regulatorResets
    };

    public string OutputDeviceId => _player.DeviceId ?? "windows-default";

    public TimeSpan BufferedDuration => _buffer.BufferedDuration;

    public static async ValueTask<WasapiAudioSink> CreateAsync(bool regulateClock = false)
    {
        BufferedWaveProvider buffer = new(
            new WaveFormat(48_000, 16, 2),
            AudioBufferPolicy.BufferCapacity)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = false,
        };
        WasapiPlayer player = await new WasapiPlayerBuilder()
            .WithDefaultDeviceStreamRouting()
            .WithLatency(60)
            .WithEventSync()
            .BuildAsync().ConfigureAwait(false);
        try
        {
            ObservedAudioProvider observed = new(buffer, regulateClock ? TimeSpan.FromMilliseconds(AudioClockRegulator.TargetBufferedMilliseconds) : TimeSpan.Zero);
            player.Init(observed);
            player.Play();
            return new WasapiAudioSink(buffer, player, observed, regulateClock ? new AudioClockRegulator() : null);
        }
        catch
        {
            await player.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask WriteAsync(
        ReadOnlyMemory<short> interleavedPcm,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlySpan<short> samples = regulator is null ? interleavedPcm.Span : regulator.Process(interleavedPcm.Span, _buffer.BufferedDuration);
        if (regulator is not null && samples.Length * sizeof(short) > _buffer.BufferLength - _buffer.BufferedBytes)
        { _buffer.ClearBuffer(); observed.RequestReprime(); regulator.Reset(); _regulatorResets++; }
        _buffer.AddSamples(MemoryMarshal.AsBytes(samples));
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteTimestampedAsync(ReadOnlyMemory<short> pcm, long presentationTimeMicroseconds, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        if (regulator?.ObserveTimestamp(presentationTimeMicroseconds, pcm.Length / 2) == true) { observed.RequestReprime(); }
        return WriteAsync(pcm, token);
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _buffer.ClearBuffer();
        observed.RequestReprime();
        regulator?.Reset();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _player.DisposeAsync().ConfigureAwait(false);
    }
}
