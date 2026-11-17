namespace VRPhoneScreenOverlay.App;

internal sealed class ApplicationInstance : IDisposable
{
    private readonly Mutex _mutex;
    private ApplicationInstance(Mutex mutex) => _mutex = mutex;
    internal static ApplicationInstance? TryAcquire(string name = @"Local\VRPhoneScreenOverlay.Application.v1")
    {
        var mutex = new Mutex(false, name);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) { return new(mutex); }
            mutex.Dispose();
            return null;
        }
        catch { mutex.Dispose(); throw; }
    }
    public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
}
