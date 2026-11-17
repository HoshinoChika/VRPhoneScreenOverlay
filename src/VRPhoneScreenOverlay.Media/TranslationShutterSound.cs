using System.Threading.Channels;

namespace VRPhoneScreenOverlay.Media;

/// <summary>Optional local cues, independent of phone audio and capture workers.</summary>
public sealed class TranslationShutterSound : IAsyncDisposable
{
    // One owner consumes at most four pending cues. Reject overflow rather than delay capture.
    private readonly Channel<bool> _cues = Channel.CreateBounded<bool>(new BoundedChannelOptions(4)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private long _rejected;
    public long Rejected => Interlocked.Read(ref _rejected);
    public TranslationShutterSound() { _worker = Task.Run(RunAsync); }
    public void Play() => Enqueue(false);
    public void PlayCountdown() => Enqueue(true);
    private void Enqueue(bool countdown)
    {
        if (!_cues.Writer.TryWrite(countdown)) { Interlocked.Increment(ref _rejected); }
    }
    private async Task RunAsync()
    {
        IAudioSink? sink = null;
        short[] beep = CreateCountdown(), shutter = LoadShutter();
        try
        {
            await foreach (bool countdown in _cues.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    sink ??= await AudioPipelineFactory.CreateDefaultWasapiSinkAsync().ConfigureAwait(false);
                    short[] samples = countdown ? beep : shutter;
                    // Feed 50 ms chunks: the sink holds only 200 ms, less than the recorded cue.
                    for (int offset = 0; offset < samples.Length; offset += 4800)
                    {
                        int count = Math.Min(4800, samples.Length - offset);
                        await sink.WriteAsync(samples.AsMemory(offset, count), _stop.Token).ConfigureAwait(false);
                        await Task.Delay(TimeSpan.FromSeconds(count / 96000d), _stop.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception exception) when (exception is AudioPipelineException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    // Cue failures never escape to screenshot/translation or the phone audio pipeline.
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { if (sink is not null) { await sink.DisposeAsync().ConfigureAwait(false); } }
    }
    private static short[] LoadShutter()
    {
        using Stream stream = typeof(TranslationShutterSound).Assembly.GetManifestResourceStream(
            "VRPhoneScreenOverlay.Media.Assets.CameraShutter.pcm")
            ?? throw new InvalidOperationException("SHUTTER_RESOURCE_MISSING");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        short[] samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        return samples;
    }
    private static short[] CreateCountdown()
    {
        int frames = 48000 * 100 / 1000;
        short[] samples = new short[frames * 2];
        for (int frame = 0; frame < frames; frame++)
        {
            double t = frame / 48000d;
            double envelope = Math.Min(1, t / 0.008) * Math.Min(1, (frames - frame) / 960d);
            double signal = Math.Sin(2 * Math.PI * 660 * t) * envelope * 0.16;
            short value = (short)(signal * short.MaxValue);
            samples[frame * 2] = value; samples[frame * 2 + 1] = value;
        }
        return samples;
    }
    public async ValueTask DisposeAsync()
    {
        _cues.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _worker.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
