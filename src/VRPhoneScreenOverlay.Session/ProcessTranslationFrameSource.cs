using System.Diagnostics;
using System.Text;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Session;

/// <summary>One short-lived background capture process; bounded pipe, no image files or shared runtime state.</summary>
public sealed class ProcessTranslationFrameSource(string executable, Func<int>? captureRangePercent = null) : IArmedTranslationFrameSource
{
    public Task<TranslationFrame> CaptureAsync(CancellationToken cancellationToken) => CaptureCoreAsync(null, null, cancellationToken);

    public Task<TranslationFrame> CaptureAsync(Task trigger, Action captured, CancellationToken cancellationToken) =>
        CaptureCoreAsync(trigger, captured, cancellationToken);

    private async Task<TranslationFrame> CaptureCoreAsync(Task? trigger, Action? captured, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = trigger is not null,
        };
        int range = captureRangePercent?.Invoke() ?? 65;
        ArgumentOutOfRangeException.ThrowIfLessThan(range, 30);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(range, 100);
        start.ArgumentList.Add("--translation-capture");
        start.ArgumentList.Add(range.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (trigger is not null) { start.ArgumentList.Add("armed"); }
        using var process = new Process { StartInfo = start };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(trigger is null ? 5 : 8));
        if (!process.Start()) { throw new InvalidOperationException("TRANSLATION_CAPTURE_FAILED"); }
        Task<string> error = ReadErrorAsync(process.StandardError, deadline.Token, captured);
        try
        {
            if (trigger is not null)
            {
                await trigger.WaitAsync(deadline.Token).ConfigureAwait(false);
                await process.StandardInput.BaseStream.WriteAsync(new byte[] { 1 }, deadline.Token).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync(deadline.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            using var output = new MemoryStream();
            byte[] buffer = new byte[8192];
            int read;
            while ((read = await process.StandardOutput.BaseStream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > 8 * 1024 * 1024) { throw new InvalidDataException("TRANSLATION_IMAGE_INVALID"); }
                await output.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false);
            }
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            string code = (await error.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(code is "TRANSLATION_VR_UNAVAILABLE" or "TRANSLATION_CAPTURE_FORMAT_UNSUPPORTED"
                    ? code : "TRANSLATION_CAPTURE_FAILED");
            }
            byte[] png = output.ToArray();
            if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            { throw new InvalidDataException("TRANSLATION_IMAGE_INVALID"); }
            return new(png, "image/png", DateTimeOffset.UtcNow);
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            try
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: false); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                // Observe the stderr reader even when stdout/caller cancellation failed.
                process.StandardError.Dispose();
                try { await error.ConfigureAwait(false); }
                catch (Exception failure) when (failure is OperationCanceledException or IOException or ObjectDisposedException) { }
            }
        }
    }

    internal static async Task<string> ReadErrorAsync(TextReader reader, CancellationToken token, Action? captured = null)
    {
        // Keep only a bounded reason prefix, but drain everything to avoid pipe deadlock.
        const int capacity = 4096;
        StringBuilder retained = new();
        char[] buffer = new char[1024];
        int count;
        bool notified = false;
        const string marker = "TRANSLATION_FRAME_CAPTURED\n";
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            retained.Append(buffer, 0, Math.Min(count, capacity - retained.Length));
            if (!notified && retained.Length >= marker.Length && retained.ToString().StartsWith(marker, StringComparison.Ordinal))
            {
                retained.Remove(0, marker.Length);
                notified = true;
                if (!token.IsCancellationRequested) { captured?.Invoke(); }
            }
        }
        return retained.ToString();
    }
}
