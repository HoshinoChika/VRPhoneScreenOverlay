using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Session;

/// <summary>Explicit, one-shot local troubleshooting capture. No request file means no image is retained.</summary>
public sealed class OneShotTranslationFrameSource(ITranslationFrameSource source, string directory) : IArmedTranslationFrameSource
{
    public async Task<TranslationFrame> CaptureAsync(CancellationToken cancellationToken)
    {
        TranslationFrame frame = await source.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return await RetainAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TranslationFrame> CaptureAsync(Task trigger, Action captured, CancellationToken cancellationToken)
    {
        TranslationFrame frame;
        if (source is IArmedTranslationFrameSource armed)
        { frame = await armed.CaptureAsync(trigger, captured, cancellationToken).ConfigureAwait(false); }
        else
        {
            await trigger.WaitAsync(cancellationToken).ConfigureAwait(false);
            frame = await source.CaptureAsync(cancellationToken).ConfigureAwait(false);
            captured();
        }
        return await RetainAsync(frame, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TranslationFrame> RetainAsync(TranslationFrame frame, CancellationToken cancellationToken)
    {
        try
        {
            string request = Path.Combine(directory, "capture-preview.request");
            if (File.Exists(request))
            {
                // Consume before writing: a failed export must not retain subsequent private frames.
                File.Delete(request);
                await File.WriteAllBytesAsync(Path.Combine(directory, "capture-preview.png"), frame.Bytes, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        { /* Optional local evidence must not break the normal translation request. */ }
        return frame;
    }
}
