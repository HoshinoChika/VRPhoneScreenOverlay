using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Session;

/// <summary>Capacity one, no waiting queue: concurrent requests are rejected, never accumulated.</summary>
public sealed class TranslationWorkflow(
    ITranslationFrameSource frames,
    ITranslationModelClient model,
    ITranslationHistory history,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private int _busy;
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public async Task<TranslationRecord?> RunAsync(TranslationMode mode, TranslationPreferences preferences,
        IProgress<TranslationProgress>? progress, Action? shutter, CancellationToken cancellationToken, Func<bool>? currentShutterSound = null, Action? countdown = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.IsValid) { throw new ArgumentOutOfRangeException(nameof(preferences)); }
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) { return null; }
        TranslationFrame? frame = null;
        using var captureLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var trigger = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<TranslationFrame>? prepared = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frames is IArmedTranslationFrameSource armed)
            {
                captureLifetime.CancelAfter(TimeSpan.FromSeconds(8));
                prepared = armed.CaptureAsync(trigger.Task, PlayShutter, captureLifetime.Token);
            }
            for (int seconds = 3; seconds > 0; seconds--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (currentShutterSound?.Invoke() ?? preferences.ShutterSound) { countdown?.Invoke(); }
                progress?.Report(new(TranslationPhase.CountingDown, seconds));
                await Task.Delay(TimeSpan.FromSeconds(1), _clock, cancellationToken);
            }
            progress?.Report(new(TranslationPhase.Capturing));
            using var captureTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            captureTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            if (prepared is not null)
            {
                captureLifetime.CancelAfter(TimeSpan.FromSeconds(5));
                trigger.TrySetResult();
                frame = await prepared;
            }
            else
            {
                frame = await frames.CaptureAsync(captureTimeout.Token);
                PlayShutter();
            }
            progress?.Report(new(TranslationPhase.Requesting));
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromMinutes(4));
            TranslationRecord result = model is IProgressTranslationModelClient reporting
                ? await reporting.AnalyzeAsync(mode, frame, progress, requestTimeout.Token)
                : await model.AnalyzeAsync(mode, frame, requestTimeout.Token);
            progress?.Report(new(TranslationPhase.Saving));
            try { await history.SaveAsync(result, preferences.HistoryLimit, cancellationToken); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { result = result with { PersistenceFailureCode = "TRANSLATION_HISTORY_WRITE_FAILED" }; }
            progress?.Report(new(TranslationPhase.Completed));
            return result;
        }
        finally
        {
            await captureLifetime.CancelAsync();
            if (prepared is not null && frame is null)
            {
                try { TranslationFrame abandoned = await prepared; Array.Clear(abandoned.Bytes); }
                catch (Exception) { /* Observe failed/cancelled preparation during countdown cleanup. */ }
            }
            if (frame is not null) { Array.Clear(frame.Bytes); }
            Volatile.Write(ref _busy, 0);
        }
        void PlayShutter()
        {
            if (cancellationToken.IsCancellationRequested || !(currentShutterSound?.Invoke() ?? preferences.ShutterSound)) { return; }
            try { shutter?.Invoke(); }
            catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
            { /* Optional sound must not abort a captured frame. */ }
        }
    }
}
