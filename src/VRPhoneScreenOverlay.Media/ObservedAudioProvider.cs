using NAudio.Wave;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Media;

// One WASAPI reader; counters only, no additional queue or per-callback allocation.
internal sealed class ObservedAudioProvider(BufferedWaveProvider source, TimeSpan initialBuffer = default) : IWaveProvider
{
    private long _requested, _delivered, _silence, _underruns, _initialSilence, _reprimeSilence;
    private bool _started;
    private int _primingWaitCallbacks;
    private long _primingWaitBytes;
    private long _primingGeneration;
    private long _completedPrimingGeneration = -1;
    // Producer increments; the reader acknowledges only the generation it observed.
    // A reset concurrent with Read must remain pending for the next callback.
    internal void RequestReprime()
    {
        if (initialBuffer > TimeSpan.Zero) { Interlocked.Increment(ref _primingGeneration); }
    }
    public WaveFormat WaveFormat => source.WaveFormat;
    public int Read(Span<byte> destination)
    {
        int alignment = WaveFormat.BlockAlign;
        long generation = Interlocked.Read(ref _primingGeneration);
        int targetBytes = (int)(initialBuffer.TotalSeconds * WaveFormat.AverageBytesPerSecond);
        // A pull larger than the production latency budget cannot be accumulated:
        // the producer clears at 160 ms. Deliver available PCM and count its real
        // shortage instead of waiting forever for an impossible priming target.
        int latencyBudgetBytes = (int)(AudioBufferPolicy.MaximumBufferedDuration.TotalSeconds * WaveFormat.AverageBytesPerSecond);
        int threshold = destination.Length > latencyBudgetBytes
            ? 0
            : Math.Min(source.BufferLength, Math.Max(targetBytes, destination.Length));
        int available = source.BufferedBytes;
        // Keep this reader-owned wait budget across producer resets: a large
        // incoming packet may clear the queue every generation before the target
        // can be reached. After two nonempty waits spanning the nominal priming duration,
        // deliver what is available. Small callbacks must still allow 50 ms.
        bool waitExpired = _primingWaitCallbacks >= 2 && _primingWaitBytes >= targetBytes && available > 0;
        if (initialBuffer > TimeSpan.Zero && generation != Interlocked.Read(ref _completedPrimingGeneration) && available < threshold && !waitExpired)
        {
            if (available > 0) { _primingWaitCallbacks++; _primingWaitBytes += destination.Length; }
            destination.Clear();
            Interlocked.Add(ref _requested, destination.Length / alignment);
            Interlocked.Add(ref _silence, destination.Length / alignment);
            if (_started) { Interlocked.Add(ref _reprimeSilence, destination.Length / alignment); }
            else { Interlocked.Add(ref _initialSilence, destination.Length / alignment); }
            return destination.Length;
        }
        Interlocked.Exchange(ref _completedPrimingGeneration, generation);
        _primingWaitCallbacks = 0;
        _primingWaitBytes = 0;
        int read = source.Read(destination);
        if (read > 0) { _started = true; }
        Interlocked.Add(ref _requested, destination.Length / alignment);
        Interlocked.Add(ref _delivered, read / alignment);
        if (read < destination.Length)
        {
            destination[read..].Clear();
            Interlocked.Add(ref _silence, (destination.Length - read) / alignment);
            if (_started) { Interlocked.Increment(ref _underruns); }
            else { Interlocked.Add(ref _initialSilence, (destination.Length - read) / alignment); }
        }
        return destination.Length;
    }
    internal AudioPlaybackDiagnostic Snapshot(double? position = null, double? latency = null) => new(
        Interlocked.Read(ref _requested), Interlocked.Read(ref _delivered), Interlocked.Read(ref _silence),
        Interlocked.Read(ref _underruns), position, latency, position.HasValue)
    { InitialSilenceFrames = Interlocked.Read(ref _initialSilence), ReprimeSilenceFrames = Interlocked.Read(ref _reprimeSilence) };
}
