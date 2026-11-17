namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Two nonfatal saved snapshots plus one reserved terminal slot; attempts are independently bounded.</summary>
internal sealed class DiagnosticCaptureBudget
{
    private int _nonfatalAttempts;
    private int _nonfatalSaved;
    private int _terminalAttempts;
    private bool _terminalSaved;
    private long _nextNonfatalAttempt;

    internal int Skipped { get; private set; }
    internal int Attempts => _nonfatalAttempts + _terminalAttempts;
    internal int Saved => _nonfatalSaved + (_terminalSaved ? 1 : 0);
    internal bool CanRetryNonfatal => _nonfatalSaved < 2 && _nonfatalAttempts < 4;

    internal int Reserve(bool terminal, long now)
    {
        if (terminal)
        {
            if (_terminalSaved || _terminalAttempts >= 2) { Skipped++; return 0; }
            _terminalAttempts++;
            return 3;
        }
        if (_nonfatalSaved >= 2 || _nonfatalAttempts >= 4 || now < _nextNonfatalAttempt)
        { Skipped++; return 0; }
        _nonfatalAttempts++;
        _nextNonfatalAttempt = now + 5000;
        return _nonfatalSaved + 1;
    }

    internal void Complete(bool terminal, bool saved)
    {
        if (!saved) { return; }
        if (terminal) { _terminalSaved = true; }
        else { _nonfatalSaved++; }
    }
}
