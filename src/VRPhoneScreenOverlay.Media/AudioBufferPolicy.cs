namespace VRPhoneScreenOverlay.Media;

public static class AudioBufferPolicy
{
    public static readonly TimeSpan BufferCapacity = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan MaximumBufferedDuration = TimeSpan.FromMilliseconds(160);

    public static bool ShouldReset(TimeSpan bufferedDuration) =>
        bufferedDuration > MaximumBufferedDuration;

    public static bool ShouldReset(TimeSpan bufferedDuration, TimeSpan incomingDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferedDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(incomingDuration, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(incomingDuration, BufferCapacity);
        // Preserve the existing latency threshold, but reserve room for the whole
        // incoming packet. BufferedWaveProvider otherwise silently drops its tail.
        return ShouldReset(bufferedDuration) || incomingDuration > BufferCapacity - bufferedDuration;
    }
}
