using System.Runtime.InteropServices;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Media;

namespace VRPhoneScreenOverlay.Session;

public enum PhoneAudioState
{
    Stopped,
    Starting,
    Playing,
    Degraded,
    Faulted,
    Stopping,
}

public sealed record PhoneAudioSnapshot(
    PhoneAudioState State,
    string ReasonCode,
    string Message,
    long DecodedPackets = 0,
    long DroppedBuffers = 0,
    TimeSpan BufferedDuration = default,
    string OutputDeviceId = "");

public sealed class PhoneAudioChangedEventArgs(PhoneAudioSnapshot snapshot) : EventArgs
{
    public PhoneAudioSnapshot Snapshot { get; } = snapshot;
}

public interface IPhoneAudioService : IAsyncDisposable
{
    public PhoneAudioSnapshot Snapshot { get; }

    public PhoneAudioSnapshot DiagnosticSnapshot { get; }

    public event EventHandler<PhoneAudioChangedEventArgs>? StateChanged;

    public ValueTask StartAsync(string? deviceKey, CancellationToken cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken);
}

public sealed class PhoneAudioService : IPhoneAudioService
{
    private const int _maximumAutomaticAttempts = 3;
    private readonly TimeSpan _retryDelay;
    private readonly IAndroidAudioSessionFactory _audioSessions;
    private readonly IAndroidConnectionLogSink _log;
    private readonly TimeSpan _startupTimeout;
    private readonly object _gate = new();
    private PhoneAudioSnapshot _snapshot = new(
        PhoneAudioState.Stopped,
        MediaReasonCodes.AudioNotStarted,
        "手机音频未运行");
    private PhoneAudioSnapshot _diagnosticSnapshot = new(
        PhoneAudioState.Stopped,
        MediaReasonCodes.AudioNotStarted,
        "手机音频未运行");
    private PhoneAudioWorker? _worker;
    private long _workerGeneration;
    private long _stopRevision;

    public PhoneAudioService(
        IAndroidAudioSessionFactory audioSessions,
        IAndroidConnectionLogSink log,
        TimeSpan? startupTimeout = null,
        TimeSpan? retryDelay = null)
    {
        _audioSessions = audioSessions ?? throw new ArgumentNullException(nameof(audioSessions));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(20);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(3);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_startupTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(_retryDelay, TimeSpan.Zero);
    }

    public PhoneAudioSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public PhoneAudioSnapshot DiagnosticSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _diagnosticSnapshot;
            }
        }
    }

    public event EventHandler<PhoneAudioChangedEventArgs>? StateChanged;

    public async ValueTask StartAsync(string? deviceKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PhoneAudioWorker worker;
        long workerGeneration;
        long stopRevision;
        while (true)
        {
            PhoneAudioWorker? completedWorker;
            lock (_gate)
            {
                completedWorker = _worker;
                if (completedWorker is null)
                {
                    worker = new PhoneAudioWorker(token => RunAsync(deviceKey, started, token));
                    _worker = worker;
                    _workerGeneration = checked(_workerGeneration + 1);
                    workerGeneration = _workerGeneration;
                    stopRevision = _stopRevision;
                    break;
                }

                if (!completedWorker.RunTask.IsCompleted)
                {
                    if (_snapshot.State is not (PhoneAudioState.Degraded or PhoneAudioState.Faulted))
                    {
                        return;
                    }
                }
            }

            await StopWorkerAsync(completedWorker).ConfigureAwait(false);
        }

        Task timeoutTask = Task.Delay(_startupTimeout, cancellationToken);
        try
        {
            Task completed = await Task.WhenAny(started.Task, worker.RunTask, timeoutTask)
                .ConfigureAwait(false);
            if (ReferenceEquals(completed, started.Task))
            {
                await started.Task.ConfigureAwait(false);
                return;
            }

            if (ReferenceEquals(completed, worker.RunTask))
            {
                await worker.RunTask.ConfigureAwait(false);
                if (worker.IsStopRequested)
                {
                    await StopWorkerAsync(worker).ConfigureAwait(false);
                    return;
                }

                await StopWorkerAsync(worker).ConfigureAwait(false);
                PublishIfLifecycleMatches(
                    workerGeneration,
                    stopRevision,
                    new PhoneAudioSnapshot(
                        PhoneAudioState.Degraded,
                        MediaReasonCodes.AudioStartEnded,
                        "手机音频启动流程提前结束，视频和控制不受影响"));
                return;
            }

            await timeoutTask.ConfigureAwait(false);
            await StopWorkerAsync(worker).ConfigureAwait(false);
            PublishIfLifecycleMatches(
                workerGeneration,
                stopRevision,
                new PhoneAudioSnapshot(
                    PhoneAudioState.Degraded,
                    MediaReasonCodes.AudioStartTimeout,
                    "手机音频启动超时，后台启动任务已停止；视频和控制不受影响"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopWorkerAsync(worker).ConfigureAwait(false);
            PublishIfLifecycleMatches(
                workerGeneration,
                stopRevision,
                new PhoneAudioSnapshot(
                    PhoneAudioState.Stopped,
                    MediaReasonCodes.AudioStartCancelled,
                    "手机音频启动已取消"));
            throw;
        }
        catch (OperationCanceledException) when (worker.IsStopRequested)
        {
            await StopWorkerAsync(worker).ConfigureAwait(false);
        }
        catch
        {
            await StopWorkerAsync(worker).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PhoneAudioWorker? worker;
        lock (_gate)
        {
            worker = _worker;
            if (worker is not null)
            {
                _stopRevision = checked(_stopRevision + 1);
            }
        }

        if (worker is null)
        {
            if (Snapshot.State != PhoneAudioState.Stopped)
            {
                Publish(new PhoneAudioSnapshot(
                    PhoneAudioState.Stopped,
                    MediaReasonCodes.AudioStopped,
                    "手机音频已停止"));
            }

            return;
        }

        Publish(Snapshot with
        {
            State = PhoneAudioState.Stopping,
            ReasonCode = MediaReasonCodes.AudioStopping,
            Message = "正在停止手机音频",
        });
        await StopWorkerAsync(worker).ConfigureAwait(false);
        Publish(new PhoneAudioSnapshot(
            PhoneAudioState.Stopped,
            MediaReasonCodes.AudioStopped,
            "手机音频已停止"));
    }

    public async ValueTask DisposeAsync() =>
        await StopAsync(CancellationToken.None).ConfigureAwait(false);

    private async Task RunAsync(
        string? deviceKey,
        TaskCompletionSource started,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= _maximumAutomaticAttempts; attempt++)
        {
            Publish(new PhoneAudioSnapshot(
                PhoneAudioState.Starting,
                MediaReasonCodes.AudioStarting,
                attempt == 1
                    ? "正在连接手机内部音频"
                    : $"正在重试手机音频（{attempt}/{_maximumAutomaticAttempts}）"));
            try
            {
                await RunSessionAsync(deviceKey, started, cancellationToken).ConfigureAwait(false);
                throw new AudioPipelineException(
                    MediaReasonCodes.AudioStreamEnded,
                    "手机音频流已结束");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                started.TrySetCanceled(cancellationToken);
                return;
            }
            catch (Exception exception) when (IsExpectedAudioFailure(exception))
            {
                (string code, string message) = MapFailure(exception);
                Publish(new PhoneAudioSnapshot(
                    PhoneAudioState.Degraded,
                    code,
                    $"{message}；视频和控制继续可用"));
                started.TrySetResult();
                _log.TryWrite(new AndroidConnectionLogEntry(
                    DateTimeOffset.UtcNow,
                    "audio_degraded",
                    code,
                    message,
                    deviceKey,
                    ExceptionType: exception.GetType().FullName,
                    ExceptionMessage: exception.Message));
                if (attempt < _maximumAutomaticAttempts)
                {
                    try
                    {
                        await Task.Delay(_retryDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
        }

        Publish(new PhoneAudioSnapshot(
            PhoneAudioState.Faulted,
            MediaReasonCodes.AudioRecoveryExhausted,
            "手机音频自动恢复失败，请等待手机重连或重新打开浮窗"));
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            "audio_faulted",
            MediaReasonCodes.AudioRecoveryExhausted,
            "手机音频自动恢复次数已耗尽",
            deviceKey));
    }

    private async Task RunSessionAsync(
        string? deviceKey,
        TaskCompletionSource started,
        CancellationToken cancellationToken)
    {
        await using IAndroidAudioSession session = await _audioSessions.OpenAsync(
            deviceKey,
            new AndroidAudioOptions(),
            cancellationToken).ConfigureAwait(false);
        if (session.Codec != AndroidAudioCodec.Opus)
        {
            throw new AudioPipelineException(
                MediaReasonCodes.AudioCodecUnsupported,
                $"当前音频编码 {session.Codec} 尚未接入播放");
        }

        await using IAudioDecoder decoder = AudioPipelineFactory.CreateOpusDecoder();
        await using IAudioSink sink = await AudioPipelineFactory.CreateDefaultWasapiSinkAsync()
            .ConfigureAwait(false);
        long decodedPackets = 0;
        long droppedBuffers = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using AndroidAudioStreamItem item = await session.ReadNextAsync(cancellationToken)
                .ConfigureAwait(false);
            if (item.Kind == AndroidAudioStreamItemKind.Configuration)
            {
                continue;
            }

            using DecodedAudioFrame frame = decoder.Decode(
                item.Payload.Span,
                item.PresentationTimeMicroseconds ?? 0);
            if (AudioBufferPolicy.ShouldReset(sink.BufferedDuration))
            {
                sink.Clear();
                droppedBuffers++;
            }

            await sink.WriteAsync(frame.Samples, cancellationToken).ConfigureAwait(false);
            decodedPackets++;
            PhoneAudioSnapshot playing = new(
                PhoneAudioState.Playing,
                MediaReasonCodes.AudioPlaying,
                "手机内部音频正在播放",
                decodedPackets,
                droppedBuffers,
                sink.BufferedDuration,
                sink.OutputDeviceId);
            Publish(playing);
            started.TrySetResult();
        }
    }

    private async ValueTask StopWorkerAsync(PhoneAudioWorker worker)
    {
        try
        {
            await worker.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                }
            }
        }
    }

    private void PublishIfLifecycleMatches(
        long expectedWorkerGeneration,
        long expectedStopRevision,
        PhoneAudioSnapshot snapshot)
    {
        bool meaningful;
        lock (_gate)
        {
            if (_workerGeneration != expectedWorkerGeneration ||
                _stopRevision != expectedStopRevision)
            {
                return;
            }

            meaningful = IsMeaningfulChange(_snapshot, snapshot);
            _snapshot = snapshot;
            RetainDiagnosticSnapshot(snapshot);
        }

        if (meaningful)
        {
            StateChanged?.Invoke(this, new PhoneAudioChangedEventArgs(snapshot));
        }
    }

    private void Publish(PhoneAudioSnapshot snapshot)
    {
        bool meaningful;
        lock (_gate)
        {
            meaningful = IsMeaningfulChange(_snapshot, snapshot);
            _snapshot = snapshot;
            RetainDiagnosticSnapshot(snapshot);
        }

        if (meaningful)
        {
            StateChanged?.Invoke(this, new PhoneAudioChangedEventArgs(snapshot));
        }
    }

    private static bool IsMeaningfulChange(
        PhoneAudioSnapshot current,
        PhoneAudioSnapshot next) =>
        current.State != next.State ||
        current.ReasonCode != next.ReasonCode ||
        next.DecodedPackets == 1 ||
        next.DecodedPackets / 100 != current.DecodedPackets / 100 ||
        next.DroppedBuffers != current.DroppedBuffers;

    private void RetainDiagnosticSnapshot(PhoneAudioSnapshot snapshot)
    {
        if (snapshot.DecodedPackets > 0 ||
            snapshot.DroppedBuffers > 0 ||
            snapshot.BufferedDuration > TimeSpan.Zero ||
            !string.IsNullOrWhiteSpace(snapshot.OutputDeviceId))
        {
            _diagnosticSnapshot = snapshot;
        }
    }

    private static bool IsExpectedAudioFailure(Exception exception) =>
        exception is AndroidConnectionException or AudioPipelineException or IOException or
            InvalidOperationException or COMException;

    private static (string Code, string Message) MapFailure(Exception exception) => exception switch
    {
        AndroidConnectionException failure => (failure.ReasonCode, failure.Message),
        AudioPipelineException failure => (failure.ReasonCode, failure.Message),
        COMException => (MediaReasonCodes.AudioOutputUnavailable, "Windows 音频输出设备暂时不可用"),
        _ => (MediaReasonCodes.AudioPipelineFailed, "手机音频链路暂时不可用"),
    };
}
