namespace VRPhoneScreenOverlay.App;

internal static class ManagedStartupBoundary
{
    internal static int Run(Func<int> run, Func<Exception, bool> record, Action<bool> showFailure)
    {
        try { return run(); }
        catch (Exception error)
        {
            bool saved = false;
            try { saved = record(error); }
            catch (Exception) { /* Failure reporting must not hide the original startup failure. */ }
            try { showFailure(saved); }
            catch (Exception) { /* WinForms itself may be unavailable; retain nonzero exit and any evidence. */ }
            return 24;
        }
    }
}
