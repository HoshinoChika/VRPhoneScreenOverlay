namespace VRPhoneScreenOverlay.SteamVR;

public enum OpenVrBindingHealthState
{
    Stopped,
    Loading,
    Ready,
    Failed,
}

public sealed record OpenVrBindingHealthSnapshot(
    OpenVrBindingHealthState State,
    bool ShowNotice,
    string ReasonCode,
    string Message)
{
    public static OpenVrBindingHealthSnapshot Stopped { get; } = new(
        OpenVrBindingHealthState.Stopped,
        false,
        OpenVrReasonCodes.BindingStopped,
        "SteamVR 手柄绑定未运行");
}

internal sealed class OpenVrBindingHealthMonitor
{
    private const long _loadingNoticeDelayMilliseconds = 1_000;
    private const long _bindingProbeIntervalMilliseconds = 1_000;
    private const long _unboundGracePeriodMilliseconds = 10_000;
    private const int _failureThreshold = 3;

    private readonly object _gate = new();
    private OpenVrBindingHealthSnapshot _snapshot = OpenVrBindingHealthSnapshot.Stopped;
    private long _startedAt;
    private long _nextProbeAt;
    private int _consecutiveFailures;
    private bool _explicitFailureObserved;

    public OpenVrBindingHealthSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Begin(long now)
    {
        lock (_gate)
        {
            _startedAt = now;
            _nextProbeAt = now;
            _consecutiveFailures = 0;
            _explicitFailureObserved = false;
            _snapshot = new OpenVrBindingHealthSnapshot(
                OpenVrBindingHealthState.Loading,
                false,
                OpenVrReasonCodes.BindingLoading,
                "正在加载 SteamVR 手柄绑定");
        }
    }

    public void Advance(long now)
    {
        lock (_gate)
        {
            if (_snapshot.State != OpenVrBindingHealthState.Loading ||
                _snapshot.ShowNotice ||
                now - _startedAt < _loadingNoticeDelayMilliseconds)
            {
                return;
            }

            _snapshot = _snapshot with { ShowNotice = true };
        }
    }

    public bool ShouldProbe(long now)
    {
        lock (_gate)
        {
            return _snapshot.State == OpenVrBindingHealthState.Loading &&
                now >= _nextProbeAt;
        }
    }

    public void ObserveBindingLoadFailed(long now)
    {
        lock (_gate)
        {
            if (_snapshot.State != OpenVrBindingHealthState.Loading)
            {
                return;
            }

            _explicitFailureObserved = true;
            _nextProbeAt = Math.Min(_nextProbeAt, now);
        }
    }

    public void RecordInitializationFailure(long now)
    {
        lock (_gate)
        {
            if (_snapshot.State == OpenVrBindingHealthState.Ready)
            {
                return;
            }

            _explicitFailureObserved = true;
            RecordFailedProbe(now);
        }
    }

    public void RecordProbe(long now, bool controllerAvailable, bool requiredBindingsComplete)
    {
        lock (_gate)
        {
            _nextProbeAt = now + _bindingProbeIntervalMilliseconds;
            if (requiredBindingsComplete)
            {
                _consecutiveFailures = 0;
                _explicitFailureObserved = false;
                _snapshot = new OpenVrBindingHealthSnapshot(
                    OpenVrBindingHealthState.Ready,
                    false,
                    OpenVrReasonCodes.BindingReady,
                    "SteamVR 手柄绑定已加载");
                return;
            }

            if (!controllerAvailable)
            {
                _consecutiveFailures = 0;
                return;
            }

            bool gracePeriodExpired = now - _startedAt >= _unboundGracePeriodMilliseconds;
            if (_explicitFailureObserved || gracePeriodExpired)
            {
                RecordFailedProbe(now);
            }
        }
    }

    private void RecordFailedProbe(long now)
    {
        _nextProbeAt = now + _bindingProbeIntervalMilliseconds;
        _consecutiveFailures++;
        if (_consecutiveFailures < _failureThreshold)
        {
            return;
        }

        _snapshot = new OpenVrBindingHealthSnapshot(
            OpenVrBindingHealthState.Failed,
            true,
            OpenVrReasonCodes.BindingLoadFailed,
            "SteamVR 手柄绑定加载失败，手机射线和控制暂不可用");
    }
}
