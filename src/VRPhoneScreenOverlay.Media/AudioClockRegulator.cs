using NAudio.Dsp;

namespace VRPhoneScreenOverlay.Media;

// One producer owns this regulator. Fixed stereo 48kHz input, <=120ms packets.
// WDL owns only its current packet/filter history; output storage is fixed at
// 6400 frames, larger than 5760 * 1.02 plus interpolation/filter history.
internal sealed class AudioClockRegulator
{
    public const double TargetBufferedMilliseconds = 50;
    private long? _expectedPts;
    public long TimestampDiscontinuities { get; private set; }
    private readonly WdlResampler _resampler = new();
    private readonly float[] _floatOutput = new float[12800];
    private readonly short[] _pcmOutput = new short[12800];
    private double _average;
    private bool _initialized;
    private int _sinceAdjustment;
    public double CorrectionRatio { get; private set; }
    public long AdjustedFrames { get; private set; }
    public double PendingMilliseconds => _resampler.GetCurrentLatency() * 1000;

    public AudioClockRegulator()
    {
        _resampler.SetMode(true, 2, false);
        _resampler.SetFeedMode(true);
        _resampler.SetRates(48000, 48000);
    }

    public ReadOnlySpan<short> Process(ReadOnlySpan<short> samples, TimeSpan buffered)
    {
        if (samples.Length == 0) { return []; }
        if (samples.Length % 2 != 0 || samples.Length > 11520) { throw new ArgumentOutOfRangeException(nameof(samples)); }
        int frames = samples.Length / 2;
        double duration = frames / 48d;
        double level = buffered.TotalMilliseconds + duration / 2;
        if (!_initialized) { _average = level; _initialized = true; }
        else { _average += (1 - Math.Exp(-duration / 2000)) * (level - _average); }
        _sinceAdjustment += frames;
        if (_sinceAdjustment >= 48000)
        {
            _sinceAdjustment -= 48000;
            double difference = TargetBufferedMilliseconds - _average;
            double threshold = CorrectionRatio == 0 ? 4 : 1;
            double correction = Math.Abs(difference) < threshold ? 0 : Math.Clamp(difference / 4000, -0.02, 0.02);
            if (correction < 0 && buffered.TotalMilliseconds + duration < TargetBufferedMilliseconds) { correction = 0; }
            CorrectionRatio = correction;
            _resampler.SetRates(48000, 48000 * (1 + correction));
        }
        int requested = _resampler.ResamplePrepare(frames, 2, out Span<float> input);
        if (requested != frames) { throw new InvalidOperationException("Audio resampler rejected bounded input."); }
        for (int i = 0; i < samples.Length; i++) { input[i] = samples[i] / 32768f; }
        int produced = _resampler.ResampleOut(_floatOutput, frames, 6400, 2);
        for (int i = 0; i < produced * 2; i++)
        { _pcmOutput[i] = (short)Math.Clamp(MathF.Round(_floatOutput[i] * 32768), short.MinValue, short.MaxValue); }
        AdjustedFrames += produced - frames;
        return _pcmOutput.AsSpan(0, produced * 2);
    }

    public bool ObserveTimestamp(long pts, int frames)
    {
        if (pts <= 0) { _expectedPts = null; return false; }
        bool discontinuity = _expectedPts is long expected && Math.Abs((double)pts - expected) > 100000;
        if (discontinuity) { Reset(); TimestampDiscontinuities++; }
        long duration = frames * 1000000L / 48000;
        _expectedPts = pts <= long.MaxValue - duration ? pts + duration : null;
        return discontinuity;
    }

    public void Reset()
    {
        _resampler.Reset(); _resampler.SetRates(48000, 48000);
        _initialized = false; _sinceAdjustment = 0; CorrectionRatio = 0; _expectedPts = null;
    }
}
