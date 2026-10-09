using System.Buffers;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace VRPhoneScreenOverlay.Media;

internal sealed class MediaFoundationH264Decoder : IH264VideoDecoder
{
    private const int _inputStreamId = 0;
    private const int _outputStreamId = 0;
    private readonly IMFTransform _transform;
    private readonly byte[] _codecConfiguration;
    private readonly int _framesPerSecond;
    private readonly int _visibleWidth;
    private readonly int _visibleHeight;
    private int _width;
    private int _height;
    private int _stride;
    private bool _prependConfiguration = true;
    private bool _mediaFoundationStarted;
    private bool _disposed;

    public MediaFoundationH264Decoder(
        int width,
        int height,
        int framesPerSecond,
        ReadOnlySpan<byte> codecConfiguration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 16_384);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 16_384);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(framesPerSecond, 120);
        if (codecConfiguration.IsEmpty)
        {
            throw new ArgumentException("H.264 codec configuration must not be empty.", nameof(codecConfiguration));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            codecConfiguration.Length,
            1024 * 1024,
            nameof(codecConfiguration));

        _width = width;
        _height = height;
        _visibleWidth = width;
        _visibleHeight = height;
        _stride = width;
        _framesPerSecond = framesPerSecond;
        _codecConfiguration = codecConfiguration.ToArray();

        try
        {
            MFStartup().CheckError();
            _mediaFoundationStarted = true;
            (IMFTransform Transform, string Name)? hardware = TryCreateHardwareDecoder();
            if (hardware is not null)
            {
                _transform = hardware.Value.Transform;
                BackendName = hardware.Value.Name;
                try
                {
                    ConfigureTransform();
                    return;
                }
                catch (Exception exception) when (
                    exception is SharpGenException or COMException or MediaDecoderException)
                {
                    _transform.Dispose();
                }
            }

            (_transform, BackendName) = CreateSoftwareDecoder();
            ConfigureTransform();
        }
        catch (Exception exception) when (exception is SharpGenException or COMException)
        {
            Dispose();
            throw new MediaDecoderException(
                MediaReasonCodes.H264InitializationFailed,
                "Windows H.264 解码器初始化失败",
                exception);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string BackendName { get; }

    public DecodedVideoFrame? DecodePacket(
        ReadOnlySpan<byte> annexBPayload,
        long presentationTimeMicroseconds,
        bool keyFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (annexBPayload.IsEmpty)
        {
            throw new ArgumentException("H.264 media packet must not be empty.", nameof(annexBPayload));
        }

        try
        {
            DecodedVideoFrame? newestFrame = DrainOutput();
            try
            {
                using IMFSample inputSample = CreateInputSample(
                    annexBPayload,
                    presentationTimeMicroseconds,
                    keyFrame);
                _transform.ProcessInput(_inputStreamId, inputSample, 0);
                DecodedVideoFrame? decodedAfterInput = DrainOutput();
                if (decodedAfterInput is not null)
                {
                    newestFrame?.Dispose();
                    newestFrame = decodedAfterInput;
                }

                DecodedVideoFrame? result = newestFrame;
                newestFrame = null;
                return result;
            }
            finally
            {
                newestFrame?.Dispose();
            }
        }
        catch (Exception exception) when (exception is SharpGenException or COMException)
        {
            throw new MediaDecoderException(
                MediaReasonCodes.H264DecodeFailed,
                "Windows H.264 解码器处理视频包失败",
                exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_transform is not null)
        {
            try
            {
                _transform.ProcessMessage(TMessageType.MessageNotifyEndOfStream, 0);
                _transform.ProcessMessage(TMessageType.MessageNotifyEndStreaming, 0);
            }
            catch (SharpGenException)
            {
            }

            _transform.Dispose();
        }

        if (_mediaFoundationStarted)
        {
            MFShutdown();
            _mediaFoundationStarted = false;
        }
    }

    public DecodedVideoFrame? Drain()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _transform.ProcessMessage(TMessageType.MessageNotifyEndOfStream, 0);
            _transform.ProcessMessage(TMessageType.MessageCommandDrain, 0);
            return DrainOutput();
        }
        catch (Exception exception) when (exception is SharpGenException or COMException)
        {
            throw new MediaDecoderException(
                MediaReasonCodes.H264DrainFailed,
                "Windows H.264 解码器结束视频流失败",
                exception);
        }
    }

    private static (IMFTransform Transform, string Name)? TryCreateHardwareDecoder()
    {
        RegisterTypeInfo inputType = new()
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = VideoFormatGuids.H264Es,
        };
        return TryActivateDecoder(
            inputType,
            EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagAsyncmft | EnumFlag.EnumFlagSortandfilter,
            "hardware");
    }

    private static (IMFTransform Transform, string Name) CreateSoftwareDecoder()
    {
        RegisterTypeInfo inputType = new()
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = VideoFormatGuids.H264Es,
        };
        (IMFTransform Transform, string Name)? decoder = TryActivateDecoder(
            inputType,
            EnumFlag.EnumFlagSyncmft | EnumFlag.EnumFlagSortandfilter,
            "software");
        if (decoder is not null)
        {
            return decoder.Value;
        }

        throw new MediaDecoderException(
            MediaReasonCodes.H264DecoderNotFound,
            "Windows 没有可用的 H.264 解码器");
    }

    private static (IMFTransform Transform, string Name)? TryActivateDecoder(
        RegisterTypeInfo inputType,
        EnumFlag flags,
        string kind)
    {
        using IMFActivateCollection activations = MFTEnumEx(
            TransformCategoryGuids.VideoDecoder,
            (uint)flags,
            inputType,
            null);
        foreach (IMFActivate activation in activations)
        {
            using (activation)
            {
                string name = string.IsNullOrWhiteSpace(activation.FriendlyName)
                    ? "Windows Media Foundation H.264"
                    : activation.FriendlyName;
                return (activation.ActivateObject<IMFTransform>(), $"{name} ({kind})");
            }
        }

        return null;
    }

    private void ConfigureTransform()
    {
        using (IMFAttributes attributes = _transform.Attributes)
        {
            attributes.Set(SinkWriterAttributeKeys.LowLatency, 1U).CheckError();
        }

        using IMFMediaType inputType = MFCreateMediaType();
        inputType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        inputType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264Es).CheckError();
        inputType.Set(
                MediaTypeAttributeKeys.InterlaceMode,
                (uint)VideoInterlaceMode.MixedInterlaceOrProgressive)
            .CheckError();
        inputType.SetBlob(MediaTypeAttributeKeys.MpegSequenceHeader, _codecConfiguration).CheckError();
        MFSetAttributeSize(
            inputType,
            MediaTypeAttributeKeys.FrameSize,
            checked((uint)_width),
            checked((uint)_height)).CheckError();
        MFSetAttributeRatio(
            inputType,
            MediaTypeAttributeKeys.FrameRate,
            checked((uint)_framesPerSecond),
            1).CheckError();
        MFSetAttributeRatio(inputType, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        _transform.SetInputType(_inputStreamId, inputType, 0);

        SelectNv12OutputType();
        _transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, 0);
        _transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, 0);
    }

    private void SelectNv12OutputType()
    {
        for (int index = 0; index < 64; index++)
        {
            IMFMediaType? candidate = null;
            try
            {
                candidate = _transform.GetOutputAvailableType(_outputStreamId, index);
                if (candidate.GetGUID(MediaTypeAttributeKeys.Subtype) != VideoFormatGuids.NV12)
                {
                    continue;
                }

                _transform.SetOutputType(_outputStreamId, candidate, 0);
                UpdateOutputGeometry(candidate);
                return;
            }
            catch (SharpGenException exception) when (
                index > 0 && exception.ResultCode == ResultCode.NoMoreTypes)
            {
                break;
            }
            finally
            {
                candidate?.Dispose();
            }
        }

        throw new MediaDecoderException(
            MediaReasonCodes.H264Nv12NotSupported,
            "Windows H.264 解码器不支持 NV12 输出");
    }

    private void UpdateOutputGeometry(IMFMediaType outputType)
    {
        if (MFGetAttributeSize(
                outputType,
                MediaTypeAttributeKeys.FrameSize,
                out uint width,
                out uint height).Success)
        {
            _width = checked((int)width);
            _height = checked((int)height);
        }

        if (outputType.GetUInt32(MediaTypeAttributeKeys.DefaultStride, out uint rawStride).Success)
        {
            _stride = Math.Abs(unchecked((int)rawStride));
        }
        else
        {
            _stride = _width;
        }
    }

    private IMFSample CreateInputSample(
        ReadOnlySpan<byte> payload,
        long presentationTimeMicroseconds,
        bool keyFrame)
    {
        bool prependConfiguration = _prependConfiguration && keyFrame;
        int totalLength = checked(payload.Length + (prependConfiguration ? _codecConfiguration.Length : 0));
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        try
        {
            buffer = MFCreateMemoryBuffer(totalLength);
            buffer.Lock(out nint data, out _, out _);
            try
            {
                unsafe
                {
                    Span<byte> destination = new((void*)data, totalLength);
                    int offset = 0;
                    if (prependConfiguration)
                    {
                        _codecConfiguration.CopyTo(destination);
                        offset = _codecConfiguration.Length;
                        _prependConfiguration = false;
                    }

                    payload.CopyTo(destination[offset..]);
                }
            }
            finally
            {
                buffer.Unlock();
            }

            buffer.CurrentLength = totalLength;
            sample = MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = checked(presentationTimeMicroseconds * 10);
            sample.SampleDuration = 10_000_000L / _framesPerSecond;
            if (keyFrame)
            {
                sample.Set(SampleAttributeKeys.CleanPoint, true).CheckError();
            }

            IMFSample result = sample;
            sample = null;
            return result;
        }
        finally
        {
            sample?.Dispose();
            buffer?.Dispose();
        }
    }

    private DecodedVideoFrame? DrainOutput()
    {
        DecodedVideoFrame? newestFrame = null;
        try
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                DecodedVideoFrame? frame = TryReadOutput(out bool needsMoreInput);
                if (frame is not null)
                {
                    newestFrame?.Dispose();
                    newestFrame = frame;
                }

                if (needsMoreInput)
                {
                    break;
                }
            }

            DecodedVideoFrame? result = newestFrame;
            newestFrame = null;
            return result;
        }
        finally
        {
            newestFrame?.Dispose();
        }
    }

    private DecodedVideoFrame? TryReadOutput(out bool needsMoreInput)
    {
        needsMoreInput = false;
        OutputStreamInfo streamInfo = _transform.GetOutputStreamInfo(_outputStreamId);
        bool transformProvidesSamples =
            ((OutputStreamInfoFlags)streamInfo.Flags & OutputStreamInfoFlags.OutputStreamProvidesSamples) != 0;
        IMFSample? suppliedSample = null;
        IMFMediaBuffer? suppliedBuffer = null;
        OutputDataBuffer output = new() { StreamID = _outputStreamId };
        try
        {
            if (!transformProvidesSamples)
            {
                int fallbackLength = checked(_stride * _height * 3 / 2);
                int outputLength = Math.Max(streamInfo.Size, fallbackLength);
                suppliedBuffer = MFCreateMemoryBuffer(outputLength);
                suppliedSample = MFCreateSample();
                suppliedSample.AddBuffer(suppliedBuffer);
                output.Sample = suppliedSample;
            }

            Result result = _transform.ProcessOutput(
                ProcessOutputFlags.None,
                1,
                ref output,
                out _);
            if (result == ResultCode.TransformNeedMoreInput)
            {
                needsMoreInput = true;
                return null;
            }

            if (result == ResultCode.TransformStreamChange)
            {
                SelectNv12OutputType();
                return null;
            }

            result.CheckError();
            IMFSample decodedSample = output.Sample ?? throw new MediaDecoderException(
                MediaReasonCodes.H264OutputMissing,
                "Windows H.264 解码器没有返回视频帧");
            return CopyDecodedFrame(decodedSample);
        }
        finally
        {
            output.Events?.Dispose();
            if (output.Sample is not null &&
                (suppliedSample is null || output.Sample.NativePointer != suppliedSample.NativePointer))
            {
                output.Sample.Dispose();
            }

            suppliedSample?.Dispose();
            suppliedBuffer?.Dispose();
        }
    }

    private unsafe DecodedVideoFrame CopyDecodedFrame(IMFSample sample)
    {
        using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
        buffer.Lock(out nint data, out _, out int currentLength);
        IMemoryOwner<byte>? owner = null;
        try
        {
            if (currentLength < 1)
            {
                throw new MediaDecoderException(
                    MediaReasonCodes.H264OutputEmpty,
                    "Windows H.264 解码器返回了空视频帧");
            }

            owner = MemoryPool<byte>.Shared.Rent(currentLength);
            new ReadOnlySpan<byte>((void*)data, currentLength).CopyTo(owner.Memory.Span);
            long presentationTime = sample.SampleTime / 10;
            DecodedVideoFrame frame = new(
                _width,
                _height,
                _stride,
                _visibleWidth,
                _visibleHeight,
                VideoPixelFormat.Nv12,
                presentationTime,
                owner,
                currentLength);
            owner = null;
            return frame;
        }
        finally
        {
            owner?.Dispose();
            buffer.Unlock();
        }
    }
}
