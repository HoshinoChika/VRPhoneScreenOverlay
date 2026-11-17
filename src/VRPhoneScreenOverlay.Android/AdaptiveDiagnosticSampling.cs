namespace VRPhoneScreenOverlay.Android;

// Serialized by the logger. At most 16 stream states; LRU eviction resets that stream's
// aggregation epoch. No queue, background worker, or retained media/content.
internal sealed class AdaptiveDiagnosticSampling
{
    private sealed class Sample(AndroidConnectionLogEntry entry)
    {
        internal AndroidConnectionLogEntry Observed = entry, Written = entry;
        internal DateTimeOffset WrittenAt = entry.Timestamp, HighUntil, NextBurst, LastIssue, LastProgress = entry.Timestamp;
        internal string Reason = "stable";
        internal long Suppressed, Bursts, Downgrades, WindowSamples;
        internal int LastInterval;
        internal double? MinFps, MaxFps, MinBitrate, MaxBitrate, MinLatency, MaxLatency;
        internal int PeakInputDepth;
    }
    private readonly Dictionary<string, Sample> _samples = [];
    internal int StreamCount => _samples.Count;
    private static string Key(AndroidConnectionLogEntry e) => e.EventName + "/" + e.DeviceKey + "/" +
        (e.EventName == "scrcpy_server_output" ? e.ServerOutput?.SessionId : e.InputPath?.SessionId);

    internal bool IsNewError(AndroidConnectionLogEntry entry, bool classified)
    {
        if (!classified || !_samples.TryGetValue(Key(entry), out Sample? previous)) { return classified; }
        if (entry.ServerOutput is { } current && previous.Observed.ServerOutput is { } old)
        {
            return current.Errors.Sum(x => x.ObservedLines) > old.Errors.Sum(x => x.ObservedLines) ||
                current.StandardOutputFailureType != old.StandardOutputFailureType ||
                current.StandardErrorFailureType != old.StandardErrorFailureType || current.ReportingFailureType != old.ReportingFailureType;
        }
        if (entry.EventName == "phone_input_path" && entry.InputPath is { } p && previous.Observed.InputPath is { } q)
        { return p.ActionReadFailures > q.ActionReadFailures || p.SinkFailures > q.SinkFailures || p.LastActionReadError != q.LastActionReadError; }
        return classified;
    }

    internal bool ShouldWrite(ref AndroidConnectionLogEntry entry, bool error)
    {
        if (entry.EventName is not ("phone_input_path" or "scrcpy_server_output" or "video_quality")) { return true; }
        string key = Key(entry);
        bool first = !_samples.TryGetValue(key, out Sample? state);
        bool reset = !first && (entry.Timestamp < state!.Observed.Timestamp ||
            entry.VideoQuality is { } v && state.Observed.VideoQuality is { } o && v.SubmittedFrames < o.SubmittedFrames ||
            entry.InputPath is { } i && state.Observed.InputPath is { } j && i.Samples < j.Samples);
        if (first || reset)
        {
            if (first && _samples.Count >= 16) { _samples.Remove(_samples.MinBy(pair => pair.Value.Observed.Timestamp).Key); }
            state = new(entry);
            _samples[key] = state;
        }
        Sample s = state!;
        DateTimeOffset now = entry.Timestamp;
        if (!first && !reset && entry.VideoQuality is { } video && s.Observed.VideoQuality is { } oldVideo)
        {
            double seconds = (now - s.Observed.Timestamp).TotalSeconds;
            if (seconds > 0)
            { entry = entry with { VideoQuality = video with { ObservedFramesPerSecond = (video.SubmittedFrames - oldVideo.SubmittedFrames) / seconds } }; }
            if (video.SubmittedFrames > oldVideo.SubmittedFrames) { s.LastProgress = now; }
        }
        double noFrames = entry.VideoQuality is null ? 0 : Math.Max(0, (now - s.LastProgress).TotalSeconds);
        bool transition = !first && !reset && Transition(s.Observed, entry);
        string? issue = error ? "error" : Detect(s.Observed, entry, noFrames);
        bool startBurst = false;
        if (issue is not null)
        {
            s.LastIssue = now;
            s.Reason = issue;
            // Ongoing faults cannot renew the same burst; a new window is rate limited.
            if (now >= s.NextBurst)
            {
                s.HighUntil = now.AddSeconds(30);
                s.NextBurst = now.AddSeconds(120);
                s.Bursts++;
                startBurst = true;
            }
        }
        bool high = now < s.HighUntil;
        bool continuing = now - s.LastIssue < TimeSpan.FromSeconds(30);
        int interval = high ? 1 : continuing ? 5 : entry.EventName == "scrcpy_server_output" ? 60 : 10;
        if (s.LastInterval == 1 && interval != 1) { s.Downgrades++; }
        s.LastInterval = interval;
        Accumulate(s, entry);
        bool write = first || reset || error || transition || issue is "input-failures" or "input-loss" or "video-overflow" || startBurst || now - s.WrittenAt >= TimeSpan.FromSeconds(interval);
        AndroidConnectionLogEntry observed = entry;
        if (write)
        {
            entry = entry with
            {
                SamplingIntervalSeconds = interval,
                SamplingReason = high ? s.Reason : continuing ? "continuing-" + s.Reason : "stable",
                SamplingWindow = new(s.WrittenAt, now, s.WindowSamples, s.Suppressed, s.Bursts, s.Downgrades,
                    reset, s.MinFps, s.MaxFps, s.MinBitrate, s.MaxBitrate, s.MinLatency, s.MaxLatency,
                    Delta(entry.VideoQuality?.SubmittedFrames, s.Written.VideoQuality?.SubmittedFrames),
                    Delta(entry.VideoQuality?.DroppedFrames, s.Written.VideoQuality?.DroppedFrames),
                    Delta(entry.InputDispatch?.AcceptedReliable, s.Written.InputDispatch?.AcceptedReliable),
                    Delta(entry.ServerOutput?.TotalLines, s.Written.ServerOutput?.TotalLines), s.PeakInputDepth, noFrames,
                    Delta(entry.ServerOutput?.Errors.Sum(x => x.ObservedLines), s.Written.ServerOutput?.Errors.Sum(x => x.ObservedLines)),
                    Delta(entry.InputDispatch?.RejectedCommands, s.Written.InputDispatch?.RejectedCommands),
                    Delta(entry.InputPath?.ActionReadFailures, s.Written.InputPath?.ActionReadFailures))
            };
            s.Written = observed;
            s.WrittenAt = now;
            s.WindowSamples = 0;
            s.MinFps = s.MaxFps = s.MinBitrate = s.MaxBitrate = s.MinLatency = s.MaxLatency = null;
            s.PeakInputDepth = 0;
        }
        else { s.Suppressed++; }
        s.Observed = observed;
        return write;
    }

    private static long? Delta(long? current, long? previous) => current.HasValue && previous.HasValue ? Math.Max(0, current.Value - previous.Value) : null;
    private static void Range(ref double? min, ref double? max, double value)
    {
        if (!double.IsFinite(value)) { return; }
        min = Math.Min(min ?? value, value); max = Math.Max(max ?? value, value);
    }
    private static void Accumulate(Sample s, AndroidConnectionLogEntry e)
    {
        s.WindowSamples++;
        if (e.VideoQuality is { } v)
        {
            Range(ref s.MinFps, ref s.MaxFps, v.ObservedFramesPerSecond ?? v.FramesPerSecond);
            Range(ref s.MinBitrate, ref s.MaxBitrate, v.BitrateMbps);
            if (v.HasLatencySamples) { Range(ref s.MinLatency, ref s.MaxLatency, v.LatencyMilliseconds); }
        }
        s.PeakInputDepth = Math.Max(s.PeakInputDepth, e.InputDispatch?.ReliableDepth ?? 0);
    }
    private static bool Transition(AndroidConnectionLogEntry before, AndroidConnectionLogEntry after) =>
        before.VideoWidth != after.VideoWidth || before.VideoHeight != after.VideoHeight ||
        before.ServerOutput is { } s && after.ServerOutput is { } t &&
        (s.StandardOutputCompleted != t.StandardOutputCompleted || s.StandardErrorCompleted != t.StandardErrorCompleted);

    private static string? Detect(AndroidConnectionLogEntry before, AndroidConnectionLogEntry after, double noFrames)
    {
        if (before.VideoQuality is { } a && after.VideoQuality is { } b)
        {
            if (b.EncodedQueueByteOverflows > a.EncodedQueueByteOverflows) { return "video-overflow"; }
            if (b.DroppedFrames > a.DroppedFrames) { return "video-drops"; }
            if (noFrames >= 3 && b.EncodedQueueBytes > 0) { return "video-suspected-stall"; }
            if (a.HasLatencySamples && b.HasLatencySamples && Math.Abs(b.LatencyMilliseconds - a.LatencyMilliseconds) >= Math.Max(50, a.LatencyMilliseconds * 0.5)) { return "video-latency"; }
        }
        if (after.InputPath is { } p && (p.LastActionReadError != 0 || p.AwaitingFreshVideoFrame)) { return "input-wait-or-error"; }
        if (before.InputPath is { } q && after.InputPath is { } r &&
            (r.ActionReadFailures > q.ActionReadFailures || r.SinkFailures > q.SinkFailures)) { return "input-failures"; }
        if (after.InputDispatch is { } e && (e.ReliableDepth >= 4 || e.FailureReason is not null)) { return "input-queue"; }
        if (before.InputDispatch is { } d && after.InputDispatch is { } f &&
            (f.RejectedCommands > d.RejectedCommands || f.DroppedMoves > d.DroppedMoves)) { return "input-loss"; }
        if (before.ServerOutput is { } s && after.ServerOutput is { } t &&
            (t.Errors.Sum(x => x.ObservedLines) > s.Errors.Sum(x => x.ObservedLines) ||
             t.StandardErrorLines > s.StandardErrorLines || t.TruncatedLines > s.TruncatedLines)) { return "server-errors"; }
        return null;
    }
}
