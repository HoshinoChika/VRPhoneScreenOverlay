using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VRPhoneScreenOverlay.Settings;

/// <summary>Durable lifecycle journal, separate from desired settings. Eight device/mode scopes.</summary>
public sealed class JsonVideoTrialStore(string path, TimeProvider? clock = null) : IVideoTrialStore, IAsyncDisposable
{
    private const int _maximumBytes = 128 * 1024;
    private const int _maximumProfiles = 8;
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _path = Path.GetFullPath(path);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static readonly string _build = typeof(JsonVideoTrialStore).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    private long? _firstFrameTick;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _snapshotGate = new();
    private VideoTrialSnapshot _snapshot = new(VideoTrialDocument.Empty, "not-initialized", false, false);
    private FileStream? _owner;
    private bool _disposed;
    private readonly object _lifetimeGate = new();
    private int _operations;
    private TaskCompletionSource? _idle;
    private Task? _disposeTask;
    public VideoTrialSnapshot Snapshot { get { lock (_snapshotGate) { return _snapshot; } } }
    public event EventHandler? Changed;
    public static JsonVideoTrialStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "video-trials.json"));

    public async ValueTask InitializeAsync(CancellationToken token)
    {
        using OperationLease operation = EnterOperation();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_owner is not null) { return; }
            AssertPlain(_path); AssertPlain(_path + ".owner");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            try { _owner = new FileStream(_path + ".owner", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) { Publish(new(VideoTrialDocument.Empty, "owner-unavailable", true, true, error.HResult, error.GetType().FullName)); return; }
            VideoTrialDocument document = VideoTrialDocument.Empty;
            if (File.Exists(_path))
            {
                await using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                if (stream.Length > _maximumBytes) { throw new InvalidDataException("Video trial journal exceeds limit."); }
                document = await JsonSerializer.DeserializeAsync<VideoTrialDocument>(stream, _json, token).ConfigureAwait(false)
                    ?? throw new InvalidDataException("Empty video trial journal.");
                Validate(document);
            }
            bool recovery = document.Active is not null || document.PendingChange is not null;
            Publish(new(document, "ready", recovery, recovery || document.RequireManualStart));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { Publish(new(VideoTrialDocument.Empty, "invalid-or-unreadable", true, true, error.HResult, error.GetType().FullName)); }
        finally { _gate.Release(); }
    }

    public async ValueTask<string> StageChangeAsync(VideoTrialContext context, VideoParameters previous, VideoParameters candidate, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        await ChangeAsync(document =>
        {
            if (document.PendingChange is not null) { throw new VideoTrialException("VIDEO_TRIAL_CHANGE_BUSY", "上一画面参数更改尚未完成"); }
            return document with { PendingChange = new(id, Scope(context), previous, candidate, _clock.GetUtcNow()) { Build = _build } };
        }, token).ConfigureAwait(false);
        return id;
    }

    public ValueTask ClearChangeAsync(string id, CancellationToken token) => ChangeAsync(document =>
        document.PendingChange is { } change && change.Id == id
            ? WithChange(document with { PendingChange = null }, change with { Outcome = "cancelled-before-start" }) : document, token);

    public async ValueTask<string> BeginAsync(VideoTrialContext context, VideoParameters desired, VideoParameters applied, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        await ChangeAsync(document =>
        {
            if (document.Active is not null) { throw new VideoTrialException("VIDEO_TRIAL_ALREADY_ACTIVE", "上一画面会话尚未结束"); }
            string scope = Scope(context);
            if (document.PendingChange is { } change)
            {
                if (change.Scope != scope || change.Candidate != desired)
                { throw new VideoTrialException("VIDEO_TRIAL_CHANGE_MISMATCH", "画面参数更改与启动请求不一致，请关闭浮窗后重试"); }
                id = change.Id;
                document = WithChange(document, change with { Outcome = "attempt-started" });
            }
            return document with
            {
                Active = new(id, scope, desired, applied, _clock.GetUtcNow(), "trying")
                {
                    Build = _build,
                    DecoderMode = context.DecoderMode == "software" ? "software" : "automatic",
                    NativeWidth = Math.Clamp(context.NativeWidth, 0, 16384),
                    NativeHeight = Math.Clamp(context.NativeHeight, 0, 16384),
                },
                PendingChange = null,
                FailureAcknowledged = false,
                RequireManualStart = false
            };
        }, token, clearAutomaticSuppression: true, committed: () => _firstFrameTick = null).ConfigureAwait(false);
        return id;
    }

    public ValueTask FirstFrameAsync(string id, VideoFrameEvidence frame, CancellationToken token)
    {
        long observedAt = _clock.GetTimestamp();
        bool first = false;
        return ChangeAsync(document =>
        {
            VideoTrialAttempt active = RequireActive(document, id);
            frame = frame with
            {
                DecoderBackend = frame.DecoderBackend[..Math.Min(frame.DecoderBackend.Length, 128)],
                GraphicsBackend = frame.GraphicsBackend[..Math.Min(frame.GraphicsBackend.Length, 128)],
                BackendNamesTruncated = frame.BackendNamesTruncated || frame.DecoderBackend.Length > 128 || frame.GraphicsBackend.Length > 128
            };
            if (active.FirstFrameAt is not null && active.Frame == frame) { return document; }
            first = active.FirstFrameAt is null;
            VideoTrialAttempt confirmed = active with { State = active.StableAt is null ? "first-frame" : "stable", FirstFrameAt = active.FirstFrameAt ?? _clock.GetUtcNow(), Frame = frame };
            VideoSuccessProfile? existing = document.Profiles.FirstOrDefault(p => p.Scope == active.Scope);
            VideoSuccessProfile profile = new(active.Scope, confirmed,
                existing?.LastFirstFrame?.Id == id ? existing.PreviousFirstFrame : existing?.LastFirstFrame, existing?.LastStable);
            return WithProfile(document with { Active = confirmed }, profile);
        }, token, committed: () => { if (first) { _firstFrameTick = observedAt; } });
    }

    public ValueTask StableAsync(string id, long submittedFrames, CancellationToken token) => ChangeAsync(document =>
    {
        VideoTrialAttempt active = RequireActive(document, id);
        if (active.FirstFrameAt is null) { throw new VideoTrialException("VIDEO_TRIAL_NO_FIRST_FRAME", "尚无已确认首帧，不能记录稳定参数"); }
        if (active.StableAt is not null) { return document; }
        double elapsed = _firstFrameTick is long firstTick ? _clock.GetElapsedTime(firstTick).TotalMilliseconds : 0;
        if (elapsed < 30000 || submittedFrames < 30)
        { throw new VideoTrialException("VIDEO_TRIAL_NOT_STABLE_YET", "需要首帧后持续运行 30 秒且提交至少 30 帧，才能记录稳定参数"); }
        VideoTrialAttempt stable = active with { State = "stable", StableAt = _clock.GetUtcNow(), StableAfterMilliseconds = elapsed, StableFrameCount = submittedFrames };
        VideoSuccessProfile existing = document.Profiles.Single(p => p.Scope == active.Scope);
        return WithProfile(document with { Active = stable }, existing with { LastFirstFrame = stable, LastStable = stable });
    }, token);

    public ValueTask EndAsync(string id, bool failed, string reason, CancellationToken token, bool requiresManualRetry = false) => ChangeAsync(document =>
    {
        VideoTrialAttempt active = RequireActive(document, id);
        string safeReason = new(reason.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').Take(96).ToArray());
        VideoTrialAttempt ended = active with
        {
            State = failed ? "failed" : "ended",
            EndReason = safeReason,
            RequiresManualRetry = failed && (requiresManualRetry || active.FirstFrameAt is null)
        };
        return WithHistory(document with { Active = null, LastAttempt = ended, FailureAcknowledged = false }, ended);
    }, token);

    public async ValueTask AcknowledgeRecoveryAsync(CancellationToken token)
    {
        if (_owner is null) { await InitializeAsync(token).ConfigureAwait(false); }
        using OperationLease operation = EnterOperation();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_owner is null) { throw new VideoTrialException("VIDEO_TRIAL_OWNER_UNAVAILABLE", "另一实例占用画面恢复记录，或记录目录不可写"); }
            VideoTrialDocument document = Snapshot.Document;
            if (Snapshot.StorageStatus != "ready" && File.Exists(_path))
            {
                AssertPlain(_path); AssertPlain(_path + ".rejected");
                File.Move(_path, _path + ".rejected", overwrite: true);
                document = VideoTrialDocument.Empty;
            }
            if (document.Active is { } interrupted)
            {
                VideoTrialAttempt ended = interrupted with { State = "interrupted", EndReason = "USER_ACKNOWLEDGED" };
                document = WithHistory(document with { LastAttempt = ended }, ended);
            }
            if (document.PendingChange is { } unfinished)
            { document = WithChange(document, unfinished with { Outcome = "interrupted-before-start" }); }
            document = document with { Active = null, PendingChange = null, FailureAcknowledged = true, RequireManualStart = true };
            await WriteAsync(document, token).ConfigureAwait(false);
            // Resolution does not unexpectedly start a phone. The next manual Begin releases this gate.
            Publish(new(document, "ready", false, true));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Publish(Snapshot with { StorageStatus = "write-failed", RecoveryRequired = true, StorageHResult = error.HResult, StorageExceptionType = error.GetType().FullName }); throw StorageError(error); }
        finally { _gate.Release(); }
    }

    public VideoRecoveryCandidate? FindRecovery(VideoTrialContext context)
    {
        VideoTrialDocument document = Snapshot.Document;
        string scope = Scope(context);
        VideoSuccessProfile? profile = document.Profiles.FirstOrDefault(p => p.Scope == scope);
        static bool Restorable(VideoParameters value) => AppSettingsPolicy.VideoBitratesMbps.Contains(value.BitrateMbps) &&
            AppSettingsPolicy.VideoMaximumFrameRates.Contains(value.MaximumFramesPerSecond);
        if (profile?.LastStable is { StableAt: not null } stable && Restorable(stable.Applied)) { return new(stable.Applied, "stable", stable.StableAt.Value); }
        bool Rejected(string id) => id == document.Active?.Id || document.RecentAttempts.Any(a => a.Id == id && a.State is "failed" or "interrupted");
        if (profile?.LastFirstFrame is { } first && !Rejected(first.Id) && Restorable(first.Applied))
        { return new(first.Applied, "first-frame", first.FirstFrameAt!.Value); }
        if (profile?.PreviousFirstFrame is { } previous && !Rejected(previous.Id) && Restorable(previous.Applied))
        { return new(previous.Applied, "first-frame", previous.FirstFrameAt!.Value); }
        VideoTrialAttempt? completed = document.RecentAttempts.FirstOrDefault(a => a.Scope == scope && a.State == "ended" && a.FirstFrameAt is not null && Restorable(a.Applied));
        return completed is null ? null : new(completed.Applied, "first-frame", completed.FirstFrameAt!.Value);
    }
    public VideoSuccessProfile? GetSuccess(VideoTrialContext context) => Snapshot.Document.Profiles.FirstOrDefault(p => p.Scope == Scope(context));
    public bool AutomaticAllowed(VideoTrialContext context, VideoParameters desired) => Snapshot.AutomaticAllowed(desired, Scope(context));

    private async ValueTask ChangeAsync(Func<VideoTrialDocument, VideoTrialDocument> change, CancellationToken token,
        bool clearAutomaticSuppression = false, Action? committed = null)
    {
        using OperationLease operation = EnterOperation();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            VideoTrialSnapshot current = Snapshot;
            if (_owner is null || current.StorageStatus != "ready")
            { throw new VideoTrialException("VIDEO_TRIAL_STORAGE_UNAVAILABLE", "画面恢复记录不可用，请在画面页处理恢复提示后重试"); }
            if (current.RecoveryRequired)
            { throw new VideoTrialException("VIDEO_TRIAL_RECOVERY_REQUIRED", "上次画面会话未正常结束，请先在画面页恢复参数或确认重试"); }
            VideoTrialDocument next = change(current.Document);
            if (ReferenceEquals(next, current.Document)) { return; }
            Validate(next);
            await WriteAsync(next, token).ConfigureAwait(false);
            committed?.Invoke();
            Publish(current with { Document = next, AutomaticSuppressed = clearAutomaticSuppression ? false : current.AutomaticSuppressed });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Publish(Snapshot with { StorageStatus = "write-failed", RecoveryRequired = true, StorageHResult = error.HResult, StorageExceptionType = error.GetType().FullName }); throw StorageError(error); }
        finally { _gate.Release(); }
    }

    private static VideoTrialDocument WithProfile(VideoTrialDocument document, VideoSuccessProfile profile)
    {
        VideoSuccessProfile[] ordered = [profile, .. document.Profiles.Where(p => p.Scope != profile.Scope)];
        return document with
        {
            Profiles = ordered.Take(_maximumProfiles).ToArray(),
            OmittedProfiles = document.OmittedProfiles + Math.Max(0, ordered.Length - _maximumProfiles)
        };
    }

    private static VideoTrialDocument WithHistory(VideoTrialDocument document, VideoTrialAttempt attempt)
    {
        VideoTrialAttempt[] history = [attempt, .. document.RecentAttempts.Where(a => a.Id != attempt.Id)];
        return document with { RecentAttempts = history.Take(8).ToArray(), OmittedAttempts = document.OmittedAttempts + Math.Max(0, history.Length - 8) };
    }

    private static VideoTrialDocument WithChange(VideoTrialDocument document, VideoParameterChange change)
    {
        VideoParameterChange[] history = [change, .. document.RecentChanges.Where(c => c.Id != change.Id)];
        return document with { RecentChanges = history.Take(8).ToArray(), OmittedChanges = document.OmittedChanges + Math.Max(0, history.Length - 8) };
    }

    private static VideoTrialAttempt RequireActive(VideoTrialDocument document, string id) => document.Active is { } active && active.Id == id
        ? active : throw new VideoTrialException("VIDEO_TRIAL_STALE_ATTEMPT", "画面尝试已变化，忽略旧会话的完成通知");

    private static string Scope(VideoTrialContext context)
    {
        int low = Math.Clamp(Math.Min(context.NativeWidth, context.NativeHeight), 0, 16384);
        int high = Math.Clamp(Math.Max(context.NativeWidth, context.NativeHeight), 0, 16384);
        string mode = context.DecoderMode == "software" ? "software" : "automatic";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{context.DeviceKey}|{low}|{high}|{mode}"))))[..32];
    }

    private async ValueTask WriteAsync(VideoTrialDocument document, CancellationToken token)
    {
        AssertPlain(_path); AssertPlain(_path + ".tmp");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, _json);
        if (bytes.Length > _maximumBytes) { throw new IOException("Video trial journal exceeds limit."); }
        try
        {
            await using (FileStream stream = new(_path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                RandomAccess.FlushToDisk(stream.SafeFileHandle);
            }
            token.ThrowIfCancellationRequested();
            File.Move(_path + ".tmp", _path, overwrite: true);
        }
        finally { if (File.Exists(_path + ".tmp")) { File.Delete(_path + ".tmp"); } }
    }

    private static void Validate(VideoTrialDocument document)
    {
        static bool Hex(string? value, int length) => value?.Length == length && value.All(c => char.IsAsciiHexDigitLower(c));
        static bool Frame(VideoFrameEvidence? value) => value is not null && value.VisibleWidth is > 0 and <= 16384 &&
            value.VisibleHeight is > 0 and <= 16384 && value.BufferWidth >= value.VisibleWidth && value.BufferWidth <= 16384 &&
            value.BufferHeight >= value.VisibleHeight && value.BufferHeight <= 16384 && value.Stride >= value.BufferWidth &&
            value.Stride <= 262144 && value.Bytes >= (long)value.Stride * value.BufferHeight * 3 / 2 &&
            value.DecoderBackend?.Length is > 0 and <= 128 && value.GraphicsBackend?.Length is > 0 and <= 128;
        static bool Attempt(VideoTrialAttempt? value) => value is null || (Hex(value.Id, 32) && Hex(value.Scope, 32) &&
            value.Desired?.IsValid == true && value.Applied?.IsValid == true &&
            value.Build?.Length is > 0 and <= 128 && value.DecoderMode is "software" or "automatic" &&
            value.NativeWidth is >= 0 and <= 16384 && value.NativeHeight is >= 0 and <= 16384 &&
            value.State is "trying" or "first-frame" or "stable" or "ended" or "failed" or "interrupted" &&
            value.EndReason?.Length is not > 96 && double.IsFinite(value.StableAfterMilliseconds) && value.StableAfterMilliseconds >= 0 &&
            (value.FirstFrameAt is null ? value.Frame is null && value.StableAt is null : Frame(value.Frame)) &&
            (value.StableAt is null || value.StableAfterMilliseconds >= 30000 && value.StableFrameCount >= 30) &&
            (value.State != "trying" || value.FirstFrameAt is null) && (value.State != "first-frame" || value.FirstFrameAt is not null) &&
            (value.State != "stable" || value.StableAt is not null));
        static bool Success(VideoTrialAttempt? value, string scope, bool stable = false) => value is null ||
            Attempt(value) && value.Scope == scope && value.FirstFrameAt is not null && (!stable || value.StableAt is not null);
        static bool Change(VideoParameterChange? value) => value is not null && Hex(value.Id, 32) && Hex(value.Scope, 32) &&
            value.Build?.Length is > 0 and <= 128 &&
            value.Previous?.IsValid == true && value.Candidate?.IsValid == true && value.Outcome is "pending" or "attempt-started" or "cancelled-before-start" or "interrupted-before-start";
        if (document.SchemaVersion != 1 || document.Profiles is null || document.Profiles.Length > _maximumProfiles ||
            document.RecentAttempts is null || document.RecentAttempts.Length > 8 || document.RecentAttempts.Any(a => a is null || !Attempt(a)) ||
            document.RecentChanges is null || document.RecentChanges.Length > 8 || document.RecentChanges.Any(c => !Change(c)) ||
            !Attempt(document.Active) || !Attempt(document.LastAttempt) ||
            document.Profiles.Any(p => p is null || !Hex(p.Scope, 32) || !Success(p.LastFirstFrame, p.Scope) || !Success(p.PreviousFirstFrame, p.Scope) || !Success(p.LastStable, p.Scope, true)) ||
            document.Profiles.Select(p => p.Scope).Distinct(StringComparer.Ordinal).Count() != document.Profiles.Length ||
            document.Active is { State: not ("trying" or "first-frame" or "stable") } ||
            document.PendingChange is { } change && !Change(change))
        { throw new InvalidDataException("Invalid video trial journal."); }
    }

    private static void AssertPlain(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Redirected video journal."); } }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private void Publish(VideoTrialSnapshot snapshot) { lock (_snapshotGate) { _snapshot = snapshot; } Changed?.Invoke(this, EventArgs.Empty); }
    private static VideoTrialException StorageError(Exception error) => new("VIDEO_TRIAL_WRITE_FAILED", "无法保存画面恢复记录，本次画面操作已停止，请检查磁盘空间和目录权限", error);
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null) { return new(_disposeTask); }
            _disposed = true;
            Task idle = _operations == 0 ? Task.CompletedTask : (_idle = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            return new(_disposeTask = DisposeWhenIdleAsync(idle));
        }
    }
    private async Task DisposeWhenIdleAsync(Task idle)
    { await idle.ConfigureAwait(false); if (_owner is not null) { await _owner.DisposeAsync().ConfigureAwait(false); } _gate.Dispose(); }
    private OperationLease EnterOperation()
    {
        lock (_lifetimeGate) { ObjectDisposedException.ThrowIf(_disposed, this); _operations++; return new(this); }
    }
    private sealed class OperationLease(JsonVideoTrialStore owner) : IDisposable
    {
        public void Dispose() { lock (owner._lifetimeGate) { if (--owner._operations == 0) { owner._idle?.TrySetResult(); } } }
    }
}
