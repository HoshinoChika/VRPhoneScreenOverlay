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
        bool softwareOnly = false, Func<(T Resource, string Name)?>? asynchronousSoftware = null) where T : class, IDisposable
        => SelectCandidates(() => [hardware], () => [software], configure, observe, releaseRejected,
            softwareOnly, asynchronousSoftware is null ? null : () => [asynchronousSoftware]);

    internal static (T Resource, string Name) SelectCandidates<T>(
        Func<IEnumerable<Func<(T Resource, string Name)?>>> hardware,
        Func<IEnumerable<Func<(T Resource, string Name)?>>> software, Action<T> configure,
        Action<DecoderInitializationEvent>? observe = null, Action<T>? releaseRejected = null,
        bool softwareOnly = false,
        Func<IEnumerable<Func<(T Resource, string Name)?>>>? asynchronousSoftware = null) where T : class, IDisposable
    {
        List<Exception> failures = [];
        (T Resource, string Name)? Try(Func<(T Resource, string Name)?> create, string kind)
        {
            T? candidate = null;
            string? name = null;
            try
            {
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
        (T Resource, string Name)? TryCategory(Func<IEnumerable<Func<(T Resource, string Name)?>>> enumerate, string kind)
        {
            (T Resource, string Name)? selected = null;
            try
            {
                observe?.Invoke(new(kind, "search-started", null));
                // Keep activation ownership alive until configuration finishes, then dispose
                // the enumeration even on early success. Unvisited candidates are never activated.
                foreach (Func<(T Resource, string Name)?> create in enumerate())
                {
                    selected = Try(create, kind);
                    if (selected is not null) { break; }
                }
            }
            catch (Exception primary)
            {
                Exception failure = primary;
                if (selected is not null)
                {
                    try
                    {
                        if (releaseRejected is null) { selected.Value.Resource.Dispose(); }
                        else { releaseRejected(selected.Value.Resource); }
                    }
                    catch (Exception cleanup) { failure = new AggregateException(primary, cleanup); }
                }
                if (primary is not (SharpGenException or COMException or MediaDecoderException))
                { ExceptionDispatchInfo.Capture(failure).Throw(); }
                failures.Add(failure);
                observe?.Invoke(new(kind, "enumeration-failed", null, failure));
                return null;
            }
            if (selected is null) { observe?.Invoke(new(kind, "exhausted", null)); }
            return selected;
        }
        if (softwareOnly) { observe?.Invoke(new("hardware", "skipped-by-preference", null)); }
        (T Resource, string Name)? result = (softwareOnly ? null : TryCategory(hardware, "hardware")) ??
            (softwareOnly || asynchronousSoftware is null ? null : TryCategory(asynchronousSoftware, "software-async")) ?? TryCategory(software, "software");
        if (result is not null) { return result.Value; }
        throw new MediaDecoderException(failures.Count == 0 ? MediaReasonCodes.H264DecoderNotFound : MediaReasonCodes.H264InitializationFailed,
            "Windows H.264 解码器初始化失败", failures.Count == 0 ? null : new AggregateException(failures));
    }
}
