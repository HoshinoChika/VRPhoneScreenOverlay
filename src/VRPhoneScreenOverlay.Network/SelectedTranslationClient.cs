using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Network;

/// <summary>Snapshots one source per request; never falls back or retries.</summary>
public sealed class SelectedTranslationClient(Func<TranslationApiSource> source, ITranslationModelClient builtIn, ITranslationModelClient personal) : IProgressTranslationModelClient
{
    public Task<TranslationRecord> AnalyzeAsync(TranslationMode mode, TranslationFrame frame, CancellationToken cancellationToken) =>
        (source() == TranslationApiSource.Personal ? personal : builtIn).AnalyzeAsync(mode, frame, cancellationToken);

    public Task<TranslationRecord> AnalyzeAsync(TranslationMode mode, TranslationFrame frame,
        IProgress<TranslationProgress>? progress, CancellationToken cancellationToken)
    {
        ITranslationModelClient selected = source() == TranslationApiSource.Personal ? personal : builtIn;
        return selected is IProgressTranslationModelClient reporting
            ? reporting.AnalyzeAsync(mode, frame, progress, cancellationToken)
            : selected.AnalyzeAsync(mode, frame, cancellationToken);
    }
}
