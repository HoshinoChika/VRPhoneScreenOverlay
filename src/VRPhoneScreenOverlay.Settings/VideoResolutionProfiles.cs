using System.Globalization;

namespace VRPhoneScreenOverlay.Settings;

public sealed record VideoResolutionProfile(
    int Percent,
    int ExpectedWidth,
    int ExpectedHeight,
    int MaximumSize,
    bool IsApproximate)
{
    public string DisplayName => Percent == 100
        ? $"原生 100%（{ExpectedWidth}×{ExpectedHeight}）"
        : $"{(IsApproximate ? "约" : string.Empty)}{Percent.ToString(CultureInfo.InvariantCulture)}%" +
          $"（{ExpectedWidth}×{ExpectedHeight}）";
}

public static class VideoResolutionProfiles
{
    private const int _minimumShortEdge = 720;
    private static readonly int[] _standardPercentages = [100, 90, 80, 70];

    public static IReadOnlyList<VideoResolutionProfile> Create(int nativeWidth, int nativeHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nativeWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(nativeHeight, 1);

        int shortEdge = Math.Min(nativeWidth, nativeHeight);
        if (shortEdge < _minimumShortEdge)
        {
            return [CreateProfile(nativeWidth, nativeHeight, 100, false)];
        }

        List<VideoResolutionProfile> profiles = [];
        foreach (int percent in _standardPercentages)
        {
            if (percent == 100 || Scale(shortEdge, percent) >= _minimumShortEdge)
            {
                profiles.Add(CreateProfile(nativeWidth, nativeHeight, percent, false));
            }
        }

        int minimumPercent = Math.Clamp(
            (int)Math.Ceiling(_minimumShortEdge * 100d / shortEdge),
            1,
            100);
        if (profiles.All(profile => profile.Percent != minimumPercent))
        {
            profiles.Add(CreateProfile(nativeWidth, nativeHeight, minimumPercent, true));
        }

        return profiles.OrderByDescending(profile => profile.Percent).ToArray();
    }

    public static VideoResolutionProfile Resolve(
        int nativeWidth,
        int nativeHeight,
        int requestedPercent)
    {
        IReadOnlyList<VideoResolutionProfile> profiles = Create(nativeWidth, nativeHeight);
        return profiles
            .OrderBy(profile => Math.Abs(profile.Percent - requestedPercent))
            .ThenByDescending(profile => profile.Percent)
            .First();
    }

    private static VideoResolutionProfile CreateProfile(
        int nativeWidth,
        int nativeHeight,
        int percent,
        bool approximate)
    {
        double scale = approximate
            ? _minimumShortEdge / (double)Math.Min(nativeWidth, nativeHeight)
            : percent / 100d;
        int width = Math.Max(
            1,
            (int)Math.Round(nativeWidth * scale, MidpointRounding.AwayFromZero));
        int height = Math.Max(
            1,
            (int)Math.Round(nativeHeight * scale, MidpointRounding.AwayFromZero));
        return new VideoResolutionProfile(
            percent,
            width,
            height,
            percent == 100 ? 0 : Math.Max(width, height),
            approximate);
    }

    private static int Scale(int value, int percent) =>
        Math.Max(1, (int)Math.Round(value * percent / 100d, MidpointRounding.AwayFromZero));
}
