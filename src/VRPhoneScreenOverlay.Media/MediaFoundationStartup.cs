namespace VRPhoneScreenOverlay.Media;

internal static class MediaFoundationStartup
{
    internal static void Run(Action initialize)
    {
        try { initialize(); }
        catch (Exception error) when (IsMissingLibrary(error))
        {
            // Only the MFStartup load boundary, never arbitrary decoder/driver failures.
            throw new MediaDecoderException("MEDIA_FOUNDATION_UNAVAILABLE", "Windows 媒体组件不可用", error);
        }
    }

    private static bool IsMissingLibrary(Exception error) => error is DllNotFoundException ||
        error is TypeInitializationException { InnerException: { } inner } && IsMissingLibrary(inner);
}
