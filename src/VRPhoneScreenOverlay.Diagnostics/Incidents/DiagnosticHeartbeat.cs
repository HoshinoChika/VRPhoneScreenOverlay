using System.Globalization;
using System.Text;

namespace VRPhoneScreenOverlay.Diagnostics;

internal enum DiagnosticHeartbeatState { Healthy, UiStale, WriterStale, Unavailable }

internal static class DiagnosticHeartbeat
{
    internal static ValueTask WriteAsync(string path, long uiPulse, long writerPulse, CancellationToken token) =>
        IncidentStorage.WriteBytesAsync(path, Encoding.UTF8.GetBytes(FormattableString.Invariant($"{writerPulse} {uiPulse}")), token, durable: false);

    internal static DiagnosticHeartbeatState Read(string path, long now)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { return DiagnosticHeartbeatState.Unavailable; }
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is < 3 or > 64) { return DiagnosticHeartbeatState.Unavailable; }
            using StreamReader reader = new(stream, Encoding.UTF8);
            string[] values = reader.ReadToEnd().Split(' ');
            if (values.Length != 2 || !long.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long writer) ||
                !long.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ui))
            { return DiagnosticHeartbeatState.Unavailable; }
            if (writer <= 0 || now < writer || now - writer > 5000) { return DiagnosticHeartbeatState.WriterStale; }
            return DiagnosticHangDetector.IsStale(ui, now) ? DiagnosticHeartbeatState.UiStale : DiagnosticHeartbeatState.Healthy;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return DiagnosticHeartbeatState.Unavailable; }
    }
}
