using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Media;

public static class Nv12Geometry
{
    public const int MaximumDimension = 16384;
    // Matches the existing dimension contract; padding cannot make a frame unbounded.
    public const int MaximumFrameBytes = MaximumDimension * MaximumDimension * 3 / 2;

    public static int Validate(VideoGeometryDiagnostic geometry, bool requireBytes = true, bool allowFinalRowWithoutPadding = false)
    {
        if (geometry.BufferWidth is < 1 or > MaximumDimension || geometry.BufferHeight is < 1 or > MaximumDimension)
        { throw Failure("MEDIA_NV12_DIMENSIONS_INVALID", geometry); }
        if ((geometry.BufferWidth & 1) != 0 || (geometry.BufferHeight & 1) != 0)
        { throw Failure("MEDIA_NV12_DIMENSIONS_ODD", geometry); }
        if (geometry.VisibleWidth < 1 || geometry.VisibleHeight < 1 || geometry.VisibleWidth > geometry.BufferWidth || geometry.VisibleHeight > geometry.BufferHeight)
        { throw Failure("MEDIA_NV12_CROP_INVALID", geometry); }
        if (geometry.Stride < geometry.BufferWidth || geometry.Stride > int.MaxValue)
        { throw Failure("MEDIA_NV12_STRIDE_INVALID", geometry); }
        long rows = geometry.BufferHeight + geometry.BufferHeight / 2;
        long required = geometry.Stride * (allowFinalRowWithoutPadding ? rows - 1 : rows) +
            (allowFinalRowWithoutPadding ? geometry.BufferWidth : 0);
        geometry = geometry with { RequiredBytes = required };
        if (required > MaximumFrameBytes)
        { throw Failure("MEDIA_NV12_BUFFER_LIMIT", geometry); }
        if (requireBytes && geometry.Bytes < required)
        { throw Failure("MEDIA_NV12_FRAME_TRUNCATED", geometry); }
        return checked((int)required);
    }

    internal static void CopyRows(ReadOnlySpan<byte> source, int sourceOffset, int pitch,
        Span<byte> destination, int width, int height, VideoGeometryDiagnostic evidence)
    {
        if (sourceOffset < 0 || sourceOffset > source.Length)
        { throw Failure("MEDIA_NV12_BUFFER_RANGE_INVALID", evidence); }
        Validate(evidence with { Stride = pitch, Bytes = source.Length - sourceOffset }, allowFinalRowWithoutPadding: true);
        int packed = Validate(evidence with { Stride = width, Bytes = destination.Length });
        if (destination.Length < packed) { throw Failure("MEDIA_NV12_FRAME_TRUNCATED", evidence); }
        for (int row = 0; row < height + height / 2; row++)
        { source.Slice(checked(sourceOffset + row * pitch), width).CopyTo(destination.Slice(row * width, width)); }
    }

    internal static MediaDecoderException Failure(string reason, VideoGeometryDiagnostic geometry) =>
        new(reason, "解码器返回的画面缓冲布局无效，已停止使用该帧") { Geometry = geometry };

    internal static (int Bytes, int AlignmentMask) Allocation(int required, int nativeBytes, int nativeAlignment, VideoGeometryDiagnostic evidence)
    {
        if (nativeBytes < 0 || nativeBytes > MaximumFrameBytes)
        { throw Failure("MEDIA_MFT_OUTPUT_ALLOCATION_INVALID", evidence with { Bytes = nativeBytes, RequiredBytes = required, Alignment = nativeAlignment }); }
        return (Math.Max(required, nativeBytes), AlignmentMask(nativeAlignment, evidence));
    }

    internal static int AlignmentMask(int nativeAlignment, VideoGeometryDiagnostic evidence, string reason = "MEDIA_MFT_OUTPUT_ALIGNMENT_INVALID")
    {
        // MFTs commonly report byte alignment; tolerate the corresponding MF mask
        // form too. At least 16-byte alignment satisfies smaller requirements.
        int alignment = nativeAlignment;
        if (alignment < 0 || alignment > 65536)
        { throw Failure(reason, evidence with { Alignment = nativeAlignment }); }
        if (alignment != 0 && (alignment & (alignment - 1)) != 0)
        {
            if ((alignment & (alignment + 1)) != 0)
            { throw Failure(reason, evidence with { Alignment = nativeAlignment }); }
            alignment++;
        }
        return Math.Max(16, alignment) - 1;
    }
}
