namespace VRPhoneScreenOverlay.Android;

// The owning logger serializes access. Video/audio/control server streams stay separate.
internal sealed class AdaptiveDiagnosticSampling
{
    private sealed record Sample(AndroidConnectionLogEntry Observed, AndroidConnectionLogEntry Written, DateTimeOffset WrittenAt);
    private readonly Dictionary<string, Sample> _samples = [];
    private DateTimeOffset _highUntil;
    private string? _reason;
    private static string Key(AndroidConnectionLogEntry entry) => entry.EventName + "/" + entry.DeviceKey + "/" + entry.ServerOutput?.SessionId;

    internal bool IsNewError(AndroidConnectionLogEntry entry, bool classified)
    {
        if (!classified || entry.ServerOutput is not { } current ||
            !_samples.TryGetValue(Key(entry), out Sample? previous) || previous.Observed.ServerOutput is not { } old)
        { return classified; }
        return current.Errors.Sum(x => x.ObservedLines) > old.Errors.Sum(x => x.ObservedLines) ||
            current.StandardOutputFailureType != old.StandardOutputFailureType ||
            current.StandardErrorFailureType != old.StandardErrorFailureType || current.ReportingFailureType != old.ReportingFailureType;
    }

    internal bool ShouldWrite(ref AndroidConnectionLogEntry entry, bool error)
    {
        DateTimeOffset now = entry.Timestamp;
        bool wasHigh = now < _highUntil;
        if (error) { _highUntil = now.AddSeconds(30); _reason = "error"; }
        if (entry.EventName is not ("phone_input_path" or "scrcpy_server_output" or "video_quality")) { return true; }
        string key = Key(entry);
        _samples.TryGetValue(key, out Sample? previous);
        if (previous is not null && entry.VideoQuality is { } video && previous.Observed.VideoQuality is { } oldVideo)
        {
            double seconds = (now - previous.Observed.Timestamp).TotalSeconds;
            if (seconds > 0 && video.SubmittedFrames >= oldVideo.SubmittedFrames)
            { entry = entry with { VideoQuality = video with { ObservedFramesPerSecond = (video.SubmittedFrames - oldVideo.SubmittedFrames) / seconds } }; }
        }
        string? fluctuation = previous is null ? null : Detect(previous.Observed, entry) ?? Detect(previous.Written, entry);
        if (fluctuation is not null)
        { _highUntil = now.AddSeconds(30); _reason = fluctuation; }
        bool high = now < _highUntil;
        int interval = high ? 1 : 10;
        bool write = error || previous is null || now < previous.WrittenAt || (!wasHigh && high) ||
            now - previous.WrittenAt >= TimeSpan.FromSeconds(interval);
        entry = entry with { SamplingIntervalSeconds = interval, SamplingReason = high ? _reason : "stable" };
        if (previous is null && _samples.Count >= 16) { _samples.Remove(_samples.MinBy(pair => pair.Value.Observed.Timestamp).Key); }
        _samples[key] = new(entry, write ? entry : previous!.Written, write ? now : previous!.WrittenAt);
        return write;
    }

    private static bool Jump(double before, double after, double absolute, double relative) =>
        double.IsFinite(before) && double.IsFinite(after) && Math.Abs(after - before) >= Math.Max(absolute, Math.Abs(before) * relative);

    private static string? Detect(AndroidConnectionLogEntry before, AndroidConnectionLogEntry after)
    {
        if (before.DeviceKey != after.DeviceKey) { return "device-changed"; }
        if (before.VideoQuality is { } a && after.VideoQuality is { } b)
        {
            if (before.VideoWidth != after.VideoWidth || before.VideoHeight != after.VideoHeight || b.SubmittedFrames < a.SubmittedFrames)
            { return "video-geometry-or-session"; }
            if (b.DroppedFrames > a.DroppedFrames) { return "video-drops"; }
            if (Jump(a.ObservedFramesPerSecond ?? a.FramesPerSecond, b.ObservedFramesPerSecond ?? b.FramesPerSecond, 5, 0.3)) { return "video-fps"; }
            if (Jump(a.LatencyMilliseconds, b.LatencyMilliseconds, 50, 0.5)) { return "video-latency"; }
            if (Jump(a.BitrateMbps, b.BitrateMbps, 2, 0.5)) { return "video-bitrate"; }
        }
        if (before.InputPath is { } p && after.InputPath is { } q)
        {
            if (p.SessionId != q.SessionId || p.CurrentBlock != q.CurrentBlock || p.AwaitingFreshVideoFrame != q.AwaitingFreshVideoFrame)
            { return "input-state"; }
            if (q.ActionReadFailures > p.ActionReadFailures || q.SinkFailures > p.SinkFailures) { return "input-failures"; }
        }
        if (before.InputDispatch is { } d && after.InputDispatch is { } e &&
            (e.RejectedCommands > d.RejectedCommands || e.DroppedMoves > d.DroppedMoves ||
             Math.Abs(e.ReliableDepth - d.ReliableDepth) >= 4 || e.FailureReason != d.FailureReason)) { return "input-queue"; }
        if (before.ServerOutput is { } s && after.ServerOutput is { } t)
        {
            if (s.SessionId != t.SessionId || s.StandardOutputCompleted != t.StandardOutputCompleted ||
                s.StandardErrorCompleted != t.StandardErrorCompleted || s.ReportingFailureType != t.ReportingFailureType ||
                s.StandardOutputFailureType != t.StandardOutputFailureType || s.StandardErrorFailureType != t.StandardErrorFailureType)
            { return "server-state"; }
            if (t.Errors.Sum(x => x.ObservedLines) > s.Errors.Sum(x => x.ObservedLines) ||
                t.StandardErrorLines > s.StandardErrorLines || t.TruncatedLines > s.TruncatedLines) { return "server-errors"; }
        }
        return null;
    }
}
