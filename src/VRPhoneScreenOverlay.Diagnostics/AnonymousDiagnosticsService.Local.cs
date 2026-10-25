namespace VRPhoneScreenOverlay.Diagnostics;

public sealed record DiagnosticLocalBundleResult(string BundlePath, string? CoverageWarning);

public sealed partial class AnonymousDiagnosticsService
{
    private readonly DiagnosticDirectoryOpener _directoryOpener = new();

    public ValueTask OpenLocalDirectoriesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _directoryOpener.OpenAsync(cancellationToken);
    }

    public async ValueTask<DiagnosticLocalBundleResult> CreateLocalReportAsync(DiagnosticUploadContext context,
        IProgress<DiagnosticProgress>? progress, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string path = await CreateLocalBundleAsync(context, progress, cancellationToken).ConfigureAwait(false);
        string? warning = await DiagnosticLocalFiles.ReadCoverageWarningAsync(path, cancellationToken).ConfigureAwait(false);
        return new(path, warning);
    }
}
