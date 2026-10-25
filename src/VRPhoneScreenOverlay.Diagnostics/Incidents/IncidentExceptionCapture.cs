using System.ComponentModel;
using System.Diagnostics;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

internal static class IncidentExceptionCapture
{
    internal static IncidentFailure Capture(Exception error, string kind, string? attemptId)
    {
        const int maximumCauses = 8;
        const int maximumFrames = 8;
        List<IncidentExceptionCause> causes = [];
        List<(Exception Error, int? Parent)> pending = [(error, null)];
        HashSet<Exception> seen = new(ReferenceEqualityComparer.Instance);
        bool truncated = false;
        for (int next = 0; next < pending.Count && causes.Count < maximumCauses; next++)
        {
            (Exception current, int? parent) = pending[next];
            if (!seen.Add(current)) { continue; }
            int index = causes.Count;
            StackFrame[] frames = new StackTrace(current, fNeedFileInfo: false).GetFrames();
            string[] names = frames.Take(maximumFrames).Select(frame => frame.GetMethod())
                .Where(method => method is not null)
                .Select(method => Bound((method!.DeclaringType?.FullName ?? "unknown") + "." + method.Name, 128)).ToArray();
            causes.Add(new(index, parent, Bound(current.GetType().FullName ?? current.GetType().Name, 128),
                current.HResult, current is Win32Exception native ? native.NativeErrorCode : null,
                names, frames.Length > maximumFrames)
            {
                Geometry = current is IVideoGeometryFailure { Geometry: { } geometry } ? geometry with
                {
                    Stage = Bound(DiagnosticSanitizer.SanitizeText(geometry.Stage ?? "unknown"), 64),
                    Layout = Bound(DiagnosticSanitizer.SanitizeText(geometry.Layout ?? "unknown"), 64)
                } : null
            });
            IEnumerable<Exception> children = current is AggregateException aggregate
                ? aggregate.InnerExceptions : current.InnerException is Exception inner ? [inner] : [];
            foreach (Exception child in children)
            {
                if (pending.Count >= maximumCauses) { truncated = true; break; }
                pending.Add((child, index));
            }
        }
        IncidentExceptionCause first = causes[0];
        return new(DateTimeOffset.UtcNow, Bound(kind, 80), first.ExceptionType,
            first.HResult, first.NativeErrorCode, first.Frames)
        {
            AttemptId = attemptId,
            Causes = causes.ToArray(),
            CausesTruncated = truncated,
        };
    }

    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
