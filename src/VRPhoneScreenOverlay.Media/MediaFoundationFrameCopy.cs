using System.Buffers;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.MediaFoundation;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Media;

internal static class MediaFoundationFrameCopy
{
    internal static unsafe DecodedVideoFrame Copy(IMFMediaBuffer buffer, int width, int height, int defaultStride,
        int visibleWidth, int visibleHeight, long presentationTime)
    {
        VideoGeometryDiagnostic geometry = new("decoder-copy", width, height, visibleWidth, visibleHeight,
            width, 0, Layout: "packed", DefaultStride: defaultStride, OutputStride: width);
        int length = Nv12Geometry.Validate(geometry, requireBytes: false);
        using IMF2DBuffer? twoD = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
        PooledFrameMemory? owner = null;
        try
        {
            if (twoD is not null)
            {
                geometry = geometry with { Stage = "decoder-2d-size", Layout = "2d-contiguous" };
                int contiguousLength = twoD.ContiguousLength;
                geometry = geometry with { Bytes = contiguousLength, RequiredBytes = length, Layout = "2d-contiguous" };
                if (contiguousLength != length)
                { throw Nv12Geometry.Failure("MEDIA_NV12_2D_SIZE_MISMATCH", geometry); }
                using IMF2DBuffer2? sized = buffer.QueryInterfaceOrNull<IMF2DBuffer2>();
                if (sized is not null)
                {
                    geometry = geometry with { Stage = "decoder-lock", Stride = 0, Bytes = 0, Layout = "2d-lock-size" };
                    sized.Lock2DSize(Buffer2DLockFlags.Read, out nint row0, out int pitch, out nint start, out int bytes);
                    Exception? copyFailure = null;
                    try
                    {
                        geometry = geometry with { Stage = "decoder-copy", Stride = pitch, Bytes = bytes, Layout = "2d-lock-size" };
                        if (start.ToInt64() <= 0 || row0.ToInt64() <= 0 || bytes < 0 || (ulong)row0 < (ulong)start || (ulong)row0 - (ulong)start > (ulong)bytes)
                        { throw Nv12Geometry.Failure("MEDIA_NV12_BUFFER_RANGE_INVALID", geometry); }
                        int offset = checked((int)((ulong)row0 - (ulong)start));
                        geometry = geometry with { Bytes = bytes - offset };
                        geometry = geometry with { RequiredBytes = Nv12Geometry.Validate(geometry, allowFinalRowWithoutPadding: true) };
                        owner = new(length);
                        Nv12Geometry.CopyRows(new ReadOnlySpan<byte>((void*)start, bytes), offset, pitch,
                            owner.Memory.Span, width, height, geometry);
                    }
                    catch (Exception error) { copyFailure = error; }
                    try { sized.Unlock2D(); }
                    catch (Exception cleanup) { copyFailure = copyFailure is null ? cleanup : new AggregateException(copyFailure, cleanup); }
                    if (copyFailure is not null) { ExceptionDispatchInfo.Capture(copyFailure).Throw(); }
                }
                else
                {
                    return CopyContiguous(twoD, width, height, defaultStride, visibleWidth, visibleHeight, presentationTime);
                }
            }
            else
            {
                geometry = geometry with { Stage = "decoder-lock", Stride = defaultStride, Layout = "linear" };
                buffer.Lock(out nint data, out int capacity, out int bytes);
                Exception? copyFailure = null;
                try
                {
                    geometry = geometry with { Stage = "decoder-copy", Stride = defaultStride, Bytes = bytes, Layout = "linear" };
                    if (data.ToInt64() <= 0 || bytes < 0 || capacity < bytes)
                    { throw Nv12Geometry.Failure("MEDIA_NV12_BUFFER_RANGE_INVALID", geometry); }
                    geometry = geometry with { RequiredBytes = Nv12Geometry.Validate(geometry, allowFinalRowWithoutPadding: true) };
                    owner = new(length);
                    Nv12Geometry.CopyRows(new ReadOnlySpan<byte>((void*)data, bytes), 0, defaultStride,
                        owner.Memory.Span, width, height, geometry);
                }
                catch (Exception error) { copyFailure = error; }
                try { buffer.Unlock(); }
                catch (Exception cleanup) { copyFailure = copyFailure is null ? cleanup : new AggregateException(copyFailure, cleanup); }
                if (copyFailure is not null) { ExceptionDispatchInfo.Capture(copyFailure).Throw(); }
            }
            DecodedVideoFrame frame = new(width, height, width, visibleWidth, visibleHeight,
                VideoPixelFormat.Nv12, presentationTime, owner ?? throw Nv12Geometry.Failure(MediaReasonCodes.H264OutputMissing, geometry), length)
            { SourceGeometry = geometry };
            owner = null;
            return frame;
        }
        catch (Exception error) when (error is SharpGenException or COMException or AggregateException)
        { throw new MediaDecoderException("MEDIA_H264_BUFFER_COPY_FAILED", "读取解码画面缓冲失败", error) { Geometry = geometry }; }
        finally { owner?.Dispose(); }
    }

    internal static DecodedVideoFrame CopyContiguous(IMF2DBuffer buffer, int width, int height,
        int defaultStride, int visibleWidth, int visibleHeight, long presentationTime)
    {
        VideoGeometryDiagnostic geometry = new("decoder-copy", width, height, visibleWidth, visibleHeight,
            width, 0, Layout: "2d-contiguous", DefaultStride: defaultStride, OutputStride: width);
        int length = Nv12Geometry.Validate(geometry, requireBytes: false);
        PooledFrameMemory? owner = null;
        try
        {
            geometry = geometry with { Bytes = buffer.ContiguousLength, RequiredBytes = length };
            if (geometry.Bytes != length) { throw Nv12Geometry.Failure("MEDIA_NV12_2D_SIZE_MISMATCH", geometry); }
            owner = new(length);
            buffer.ContiguousCopyTo(owner.Array, length);
            DecodedVideoFrame frame = new(width, height, width, visibleWidth, visibleHeight,
                VideoPixelFormat.Nv12, presentationTime, owner, length)
            { SourceGeometry = geometry };
            owner = null;
            return frame;
        }
        catch (Exception error) when (error is SharpGenException or COMException)
        { throw new MediaDecoderException("MEDIA_H264_BUFFER_COPY_FAILED", "读取解码画面缓冲失败", error) { Geometry = geometry }; }
        finally { owner?.Dispose(); }
    }

    private sealed class PooledFrameMemory(int length) : IMemoryOwner<byte>
    {
        private byte[]? _array = ArrayPool<byte>.Shared.Rent(length);
        public byte[] Array => _array ?? throw new ObjectDisposedException(nameof(PooledFrameMemory));
        public Memory<byte> Memory => Array.AsMemory(0, length);
        public void Dispose() { byte[]? array = Interlocked.Exchange(ref _array, null); if (array is not null) { ArrayPool<byte>.Shared.Return(array); } }
    }
}
