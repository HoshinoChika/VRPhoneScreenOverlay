using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Core;

namespace VRPhoneScreenOverlay.App;

/// <summary>Owns acquired components in reverse order, including partially initialized startup.</summary>
internal sealed class ApplicationLifetime(IIncidentRecorder? incident = null) : IAsyncDisposable
{
    private readonly List<ShutdownStep> _steps = [];
    private bool _stopped;
    private readonly object _gate = new();
    private Task? _disposeTask;
    private bool _healthy;
    public IReadOnlyList<string> Failures { get; private set; } = [];
    public void Register(string name, Func<ValueTask> stop)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            _steps.Add(new(name, stop));
        }
    }
    public void ConfirmWhenShown(Form form, Action confirm)
    {
        form.Shown += (_, _) => form.BeginInvoke(() =>
        {
            if (!form.IsDisposed && !form.Disposing) { ConfirmHealthy(confirm); }
        });
    }
    public void ConfirmHealthy(Action confirm)
    {
        if (_stopped || _healthy) { return; }
        confirm();
        _healthy = true;
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _stopped = true;
            return new(_disposeTask ??= StopAsync());
        }
    }

    private async Task StopAsync() =>
        Failures = await ShutdownSequence.RunAsync(_steps.AsEnumerable().Reverse(), incident).ConfigureAwait(false);
}
