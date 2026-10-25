using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using SharpGen.Runtime;

namespace VRPhoneScreenOverlay.Media;

public sealed record DecoderInitializationEvent(string Kind, string Phase, string? Backend, Exception? Failure = null);

internal static class DecoderCandidateSelection
{
    internal static (T Resource, string Name) Select<T>(Func<(T Resource, string Name)?> hardware,
        Func<(T Resource, string Name)?> software, Action<T> configure,
        Action<DecoderInitializationEvent>? observe = null, Action<T>? releaseRejected = null,
        bool softwareOnly = false) where T : class, IDisposable
    {
        List<Exception> failures = [];
        (T Resource, string Name)? Try(Func<(T Resource, string Name)?> create, string kind)
        {
            T? candidate = null;
            string? name = null;
            try
            {
                observe?.Invoke(new(kind, "search-started", null));
                (T Resource, string Name)? found = create();
                if (found is null)
                {
                    observe?.Invoke(new(kind, "not-found", null));
                    return null;
                }
                (candidate, name) = found.Value;
                observe?.Invoke(new(kind, "configure-started", name));
                configure(candidate);
                observe?.Invoke(new(kind, "configure-succeeded", name));
                // Ownership transfers only after all configuration has succeeded.
                (T Resource, string Name) selected = (candidate, name);
                candidate = null;
                return selected;
            }
            catch (Exception primary)
            {
                Exception failure = primary;
                if (candidate is not null)
                {
                    try { if (releaseRejected is null) { candidate.Dispose(); } else { releaseRejected(candidate); } }
                    catch (Exception cleanup) { failure = new AggregateException(primary, cleanup); }
                }
                if (primary is not (SharpGenException or COMException or MediaDecoderException))
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }
                failures.Add(failure);
                observe?.Invoke(new(kind, "failed", name, failure));
                return null;
            }
        }
        if (softwareOnly) { observe?.Invoke(new("hardware", "skipped-by-preference", null)); }
        (T Resource, string Name)? result = (softwareOnly ? null : Try(hardware, "hardware")) ?? Try(software, "software");
        if (result is not null) { return result.Value; }
        throw new MediaDecoderException(failures.Count == 0 ? MediaReasonCodes.H264DecoderNotFound : MediaReasonCodes.H264InitializationFailed,
            "Windows H.264 解码器初始化失败", failures.Count == 0 ? null : new AggregateException(failures));
    }
}
