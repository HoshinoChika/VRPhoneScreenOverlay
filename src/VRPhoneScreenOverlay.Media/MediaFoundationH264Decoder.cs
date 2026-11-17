using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.MediaFoundation;
using VRPhoneScreenOverlay.Contracts;
using static Vortice.MediaFoundation.MediaFactory;

namespace VRPhoneScreenOverlay.Media;

internal sealed class MediaFoundationH264Decoder : IH264VideoDecoder
{
    private int _inputStreamId;
    private int _outputStreamId;
    private readonly IMFTransform _transform;
    private readonly byte[] _codecConfiguration;
    private readonly int _framesPerSecond;
    private readonly int _visibleWidth;
    private readonly int _visibleHeight;
    private int _width;
    private int _height;
    private int _stride;
    private VideoColorMetadata _color;
    private int _inputAlignmentMask = 15;
    private int _inputMinimumBytes;
    private bool _prependConfiguration = true;
    private bool _mediaFoundationStarted;
    private volatile bool _disposed;
    private readonly object _nativeGate = new();
    private bool _asynchronous;
    private readonly MftAsyncFlow? _asyncFlow;
    private readonly IMFMediaEventGenerator? _events;
    private readonly MftEventPump? _eventPump;
    // Capacity one, replace oldest decoded output; producer owns until TakeOutput.
    private DecodedVideoFrame? _pendingOutput;
    private long _replacedOutput;
    private long _outputEvents;
    private long _outputSequence;
    private int _consecutiveFormatChanges;
    private int _finalResourcesReleased;
    private int _nativeShutdownCompleted;
    private Exception? _eventCleanupFailure;

    public event EventHandler? OutputAvailable;
    public long ReplacedOutputFrames => Interlocked.Read(ref _replacedOutput);
    public string DiagnosticState => $"async={_asynchronous}; inputEvents={_asyncFlow?.InputEvents ?? 0}; inputConsumed={_asyncFlow?.InputConsumed ?? 0}; outputEvents={Interlocked.Read(ref _outputEvents)}; replaced={ReplacedOutputFrames}";

    public MediaFoundationH264Decoder(
        int width,
        int height,
        int framesPerSecond,
        ReadOnlySpan<byte> codecConfiguration,
        Action<DecoderInitializationEvent>? observe = null,
        VideoDecoderPreference preference = VideoDecoderPreference.Automatic)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 16_384);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 16_384);
        ArgumentOutOfRangeException.ThrowIfLessThan(framesPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(framesPerSecond, 120);
        if (!Enum.IsDefined(preference)) { throw new ArgumentOutOfRangeException(nameof(preference)); }
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
            MediaFoundationStartup.Run(() => MFStartup().CheckError());
            _mediaFoundationStarted = true;
            (_transform, BackendName) = DecoderCandidateSelection.SelectCandidates(
                () => EnumerateDecoders(EnumFlag.EnumFlagHardware, "hardware", observe),
                () => EnumerateDecoders(EnumFlag.EnumFlagSyncmft, "software", observe),
                candidate =>
                {
                    // A rejected candidate may have selected padded output geometry.
                    _width = width; _height = height; _stride = width;
                    ConfigureTransform(candidate, observe);
                }, observe, ReleaseTransform, preference == VideoDecoderPreference.Software,
                () => EnumerateDecoders(EnumFlag.EnumFlagAsyncmft, "software-async", observe));
            if (_asynchronous)
            {
                _asyncFlow = new();
                _events = _transform.QueryInterface<IMFMediaEventGenerator>();
                _eventPump = new MftEventPump(ReadEvent, _asyncFlow, _nativeGate, ReceiveNotifiedOutput,
                    NotifyOutput, OnEventFailure, OnEventThreadFinished);
            }
        }
        catch (Exception primary)
        {
            try { Dispose(); }
            catch (Exception cleanup)
            {
                throw new MediaDecoderException(MediaReasonCodes.H264InitializationFailed,
                    "Windows H.264 解码器初始化失败", new AggregateException(primary, cleanup));
            }
            if (primary is SharpGenException or COMException)
            { throw new MediaDecoderException(MediaReasonCodes.H264InitializationFailed, "Windows H.264 解码器初始化失败", primary); }
            throw;
        }
    }

    public string BackendName { get; }

    public async ValueTask<DecodedVideoFrame?> DecodePacketAsync(ReadOnlyMemory<byte> payload,
        long presentationTimeMicroseconds, bool keyFrame, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_asynchronous) { return DecodeSynchronous(payload.Span, presentationTimeMicroseconds, keyFrame, token); }
        if (payload.IsEmpty) { throw new ArgumentException("Empty video packet.", nameof(payload)); }
        await _asyncFlow!.WaitForInputAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
        lock (_nativeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _asyncFlow.ThrowIfFailed();
            token.ThrowIfCancellationRequested();
            try
            {
                using IMFSample sample = CreateInputSample(payload.Span, presentationTimeMicroseconds, keyFrame);
                _transform.ProcessInput(_inputStreamId, sample, 0);
            }
            catch (Exception error) when (error is SharpGenException or COMException)
            { throw new MediaDecoderException(MediaReasonCodes.H264DecodeFailed, "异步解码器提交输入失败", error); }
        }
        return TakeOutput();
    }

    public DecodedVideoFrame? TakeOutput()
    {
        lock (_nativeGate)
        {
            _asyncFlow?.ThrowIfFailed();
            DecodedVideoFrame? result = _pendingOutput;
            _pendingOutput = null;
            return result;
        }
    }

    private MftEvent ReadEvent()
    {
        using IMFMediaEvent notification = _events!.GetEvent(0);
        return MftEventPump.Parse(notification);
    }

    private void ReceiveNotifiedOutput()
    {
        Interlocked.Increment(ref _outputEvents);
        DecodedVideoFrame? decoded = ReadNotifiedOutput();
        try
        {
            if (decoded is null) { return; }
            if (_pendingOutput is not null) { _pendingOutput.Dispose(); Interlocked.Increment(ref _replacedOutput); }
            _pendingOutput = decoded;
            decoded = null;
        }
        finally { decoded?.Dispose(); }
    }

    private void OnEventFailure(Exception error)
    {
        if (_disposed) { return; }
        _asyncFlow!.Fail(error is MediaDecoderException ? error : new MediaDecoderException(
            "MEDIA_MFT_EVENT_FAILED", "解码器事件处理失败", error));
        NotifyOutput();
    }

    private void OnEventThreadFinished()
    {
        // The reader may exit immediately after RequestStop, before the owner has
        // sent native shutdown. Only the owner may release platform state then.
        if (!_disposed || Volatile.Read(ref _nativeShutdownCompleted) == 0) { return; }
        try { ReleaseFinalResources(); }
        catch (Exception error) { _eventCleanupFailure = error; }
    }

    private void NotifyOutput()
    {
        try { OutputAvailable?.Invoke(this, EventArgs.Empty); }
        catch (Exception error) { _asyncFlow!.Fail(new MediaDecoderException("MEDIA_MFT_OUTPUT_CONSUMER_FAILED", "解码输出接收失败", error)); }
    }

    private DecodedVideoFrame? ReadNotifiedOutput()
    {
        // Exactly one ProcessOutput per native HaveOutput event, including a
        // format-change result. Renegotiate, then wait for another output event.
        DecodedVideoFrame? frame = null;
        try
        {
            frame = TryReadOutput(out bool needsInput);
            if (needsInput)
            { throw new MediaDecoderException("MEDIA_MFT_OUTPUT_WITHOUT_SAMPLE", "异步解码输出事件未提供有效输出"); }
            if (frame is null)
            {
                if (++_consecutiveFormatChanges > 4)
                { throw new MediaDecoderException("MEDIA_MFT_FORMAT_CHANGE_LIMIT", "解码器输出格式反复变化"); }
                return null;
            }
            _consecutiveFormatChanges = 0;
            DecodedVideoFrame result = frame;
            frame = null;
            return result;
        }
        finally { frame?.Dispose(); }
    }

    private static void ReleaseTransform(IMFTransform transform)
    {
        Exception? failure = null;
        try
        {
            using IMFShutdown? shutdown = transform.QueryInterfaceOrNull<IMFShutdown>();
            shutdown?.Shutdown();
        }
        catch (Exception error) when (error is SharpGenException or COMException) { failure = error; }
        try { transform.Dispose(); }
        catch (Exception error) when (error is SharpGenException or COMException)
        { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null)
        { throw new MediaDecoderException(MediaReasonCodes.H264CleanupFailed, "Windows H.264 解码器释放失败", failure); }
    }

    public DecodedVideoFrame? DecodePacket(
        ReadOnlySpan<byte> annexBPayload,
        long presentationTimeMicroseconds,
        bool keyFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_asynchronous)
        {
            // Compatibility entry point; the production pipeline uses the memory/
            // ValueTask overload and never copies or blocks a task per video frame.
            return DecodePacketAsync(annexBPayload.ToArray(), presentationTimeMicroseconds, keyFrame,
                CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        return DecodeSynchronous(annexBPayload, presentationTimeMicroseconds, keyFrame, CancellationToken.None);
    }

    private DecodedVideoFrame? DecodeSynchronous(ReadOnlySpan<byte> annexBPayload,
        long presentationTimeMicroseconds, bool keyFrame, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (annexBPayload.IsEmpty)
        {
            throw new ArgumentException("H.264 media packet must not be empty.", nameof(annexBPayload));
        }

        try
        {
            DecodedVideoFrame? newestFrame = DrainOutput(token);
            try
            {
                using IMFSample inputSample = CreateInputSample(
                    annexBPayload,
                    presentationTimeMicroseconds,
                    keyFrame);
                _transform.ProcessInput(_inputStreamId, inputSample, 0);
                DecodedVideoFrame? decodedAfterInput = DrainOutput(token);
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
        _asyncFlow?.Close();
        _eventPump?.RequestStop();
        List<Exception> failures = [];
        lock (_nativeGate)
        {
            _pendingOutput?.Dispose();
            _pendingOutput = null;
            if (_transform is not null)
            {
                foreach (TMessageType message in new[] { TMessageType.MessageCommandFlush, TMessageType.MessageNotifyEndOfStream, TMessageType.MessageNotifyEndStreaming })
                {
                    try { _transform.ProcessMessage(message, 0); }
                    catch (Exception error) when (error is SharpGenException or COMException) { failures.Add(error); }
                }
                try { ReleaseTransform(_transform); }
                catch (Exception error) when (error is SharpGenException or COMException or MediaDecoderException) { failures.Add(error); }
            }
            Volatile.Write(ref _nativeShutdownCompleted, 1);
        }
        if (_eventPump is not null && !_eventPump.IsCurrentThread && _eventPump.IsAlive && !_eventPump.Join(TimeSpan.FromSeconds(2)))
        { failures.Add(new MediaDecoderException("MEDIA_MFT_EVENT_STOP_TIMEOUT", "解码事件线程未及时退出，资源等待该线程结束后释放")); }
        else if (_eventPump is null || !_eventPump.IsCurrentThread)
        {
            try { ReleaseFinalResources(); }
            catch (Exception error) { failures.Add(error); }
            if (_eventCleanupFailure is not null) { failures.Add(_eventCleanupFailure); }
        }
        if (failures.Count > 0)
        { throw new MediaDecoderException(MediaReasonCodes.H264CleanupFailed, "Windows H.264 解码器清理未全部完成", new AggregateException(failures)); }
    }

    private void ReleaseFinalResources()
    {
        if (Interlocked.Exchange(ref _finalResourcesReleased, 1) != 0) { return; }
        _events?.Dispose();
        _asyncFlow?.Dispose();
        if (_mediaFoundationStarted)
        {
            _mediaFoundationStarted = false;
            MFShutdown().CheckError();
        }
    }

    public async ValueTask<DecodedVideoFrame?> DrainAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_asynchronous) { return DrainSynchronous(token); }
        _asyncFlow!.BeginDrain();
        lock (_nativeGate)
        {
            _transform.ProcessMessage(TMessageType.MessageNotifyEndOfStream, 0);
            _transform.ProcessMessage(TMessageType.MessageCommandDrain, 0);
        }
        await _asyncFlow.WaitForDrainAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
        return TakeOutput();
    }

    public DecodedVideoFrame? Drain()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_asynchronous) { return DrainAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult(); }
        return DrainSynchronous(CancellationToken.None);
    }

    private DecodedVideoFrame? DrainSynchronous(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            _transform.ProcessMessage(TMessageType.MessageNotifyEndOfStream, 0);
            _transform.ProcessMessage(TMessageType.MessageCommandDrain, 0);
            return DrainOutput(token);
        }
        catch (Exception exception) when (exception is SharpGenException or COMException)
        {
            throw new MediaDecoderException(
                MediaReasonCodes.H264DrainFailed,
                "Windows H.264 解码器结束视频流失败",
                exception);
        }
    }

    private static IEnumerable<Func<(IMFTransform Resource, string Name)?>> EnumerateDecoders(
        EnumFlag category, string kind, Action<DecoderInitializationEvent>? observe)
    {
        RegisterTypeInfo inputType = new()
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = VideoFormatGuids.H264Es,
        };
        using IMFActivateCollection activations = MFTEnumEx(
            TransformCategoryGuids.VideoDecoder,
            (uint)(category | EnumFlag.EnumFlagSortandfilter),
            inputType,
            null);
        foreach (IMFActivate activation in activations)
        {
            using (activation)
            {
                // Activation and configuration happen while this iterator owns the activation.
                yield return () =>
                {
                    string name = string.IsNullOrWhiteSpace(activation.FriendlyName)
                        ? "Windows Media Foundation H.264"
                        : activation.FriendlyName;
                    observe?.Invoke(new(kind, "activation-started", name));
                    return (activation.ActivateObject<IMFTransform>(), $"{name} ({kind})");
                };
            }
        }
    }

    private void ConfigureTransform(IMFTransform transform, Action<DecoderInitializationEvent>? observe)
    {
        using (IMFAttributes attributes = transform.Attributes)
        {
            Result asyncAttribute = attributes.GetUInt32(TransformAttributeKeys.TransformAsync, out uint asynchronous);
            if (asyncAttribute.Failure && asyncAttribute != ResultCode.Attributenotfound) { asyncAttribute.CheckError(); }
            _asynchronous = asyncAttribute.Success && asynchronous != 0;
            observe?.Invoke(new("protocol", _asynchronous ? "asynchronous" : "synchronous", null));
            if (_asynchronous)
            {
                attributes.Set(TransformAttributeKeys.TransformAsyncUnlock, 1U).CheckError();
                using IMFMediaEventGenerator events = transform.QueryInterface<IMFMediaEventGenerator>();
                using IMFShutdown shutdown = transform.QueryInterface<IMFShutdown>();
            }
            attributes.Set(SinkWriterAttributeKeys.LowLatency, 1U).CheckError();
        }

        transform.GetStreamCount(out int inputCount, out int outputCount);
        (_inputStreamId, _outputStreamId) = MftStreamIdentifiers.Resolve(inputCount, outputCount,
            (inputs, outputs) => transform.GetStreamIDs(inputs.Length, inputs, outputs.Length, outputs));

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
        transform.SetInputType(_inputStreamId, inputType, 0);

        SelectNv12OutputType(transform);
        InputStreamInfo inputInfo = transform.GetInputStreamInfo(_inputStreamId);
        VideoGeometryDiagnostic inputAllocation = new("decoder-input-type", _width, _height, _visibleWidth,
            _visibleHeight, 0, inputInfo.Size, Layout: "h264-annex-b", Alignment: inputInfo.Alignment);
        if (inputInfo.Size is < 0 or > 32 * 1024 * 1024)
        { throw Nv12Geometry.Failure("MEDIA_MFT_INPUT_ALLOCATION_INVALID", inputAllocation); }
        _inputMinimumBytes = inputInfo.Size;
        _inputAlignmentMask = Nv12Geometry.AlignmentMask(inputInfo.Alignment, inputAllocation, "MEDIA_MFT_INPUT_ALIGNMENT_INVALID");
        transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, 0);
        transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, 0);
    }

    private void SelectNv12OutputType(IMFTransform transform)
    {
        for (int index = 0; index < 64; index++)
        {
            IMFMediaType? candidate = null;
            try
            {
                candidate = transform.GetOutputAvailableType(_outputStreamId, index);
                if (candidate.GetGUID(MediaTypeAttributeKeys.Subtype) != VideoFormatGuids.NV12)
                {
                    continue;
                }

                transform.SetOutputType(_outputStreamId, candidate, 0);
                using IMFMediaType negotiated = transform.GetOutputCurrentType(_outputStreamId);
                UpdateOutputGeometry(negotiated);
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
        long nextWidth = _width;
        long nextHeight = _height;
        Result size = MFGetAttributeSize(
                outputType,
                MediaTypeAttributeKeys.FrameSize,
                out uint width,
                out uint height);
        if (size.Success)
        {
            nextWidth = width;
            nextHeight = height;
        }
        else if (size != ResultCode.Attributenotfound) { size.CheckError(); }
        Result strideResult = outputType.GetUInt32(MediaTypeAttributeKeys.DefaultStride, out uint rawStride);
        if (strideResult.Failure && strideResult != ResultCode.Attributenotfound) { strideResult.CheckError(); }
        long nextStride = strideResult.Success ? unchecked((int)rawStride) : nextWidth;
        Nv12Geometry.Validate(new("decoder-output-type", nextWidth, nextHeight, _visibleWidth, _visibleHeight,
            nextStride, 0, Layout: "media-type", DefaultStride: nextStride), requireBytes: false);
        _color = MediaFoundationVideoColor.Read(outputType);
        _width = checked((int)nextWidth);
        _height = checked((int)nextHeight);
        _stride = checked((int)nextStride);
    }

    private IMFSample CreateInputSample(
        ReadOnlySpan<byte> payload,
        long presentationTimeMicroseconds,
        bool keyFrame)
    {
        bool prependConfiguration = _prependConfiguration && keyFrame;
        long requestedLength = (long)payload.Length + (prependConfiguration ? _codecConfiguration.Length : 0);
        // Transport payloads are capped at 16 MiB, plus at most 1 MiB configuration.
        if (requestedLength > 17 * 1024 * 1024)
        { throw Nv12Geometry.Failure("MEDIA_H264_PACKET_LIMIT", new("decoder-input", _width, _height, _visibleWidth, _visibleHeight, 0, requestedLength, Layout: "h264-annex-b")); }
        int totalLength = checked((int)requestedLength);
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        try
        {
            buffer = MFCreateAlignedMemoryBuffer(Math.Max(totalLength, _inputMinimumBytes), _inputAlignmentMask);
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

    private DecodedVideoFrame? DrainOutput(CancellationToken token)
    {
        DecodedVideoFrame? newestFrame = null;
        long started = Stopwatch.GetTimestamp();
        bool exhausted = true;
        try
        {
            for (int attempt = 0; attempt < 128; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(2))
                { throw new MediaDecoderException("MEDIA_MFT_SYNC_OUTPUT_TIMEOUT", "同步解码输出未及时结束"); }
                DecodedVideoFrame? frame = TryReadOutput(out bool needsMoreInput);
                if (frame is not null)
                {
                    newestFrame?.Dispose();
                    newestFrame = frame;
                }

                if (needsMoreInput)
                {
                    exhausted = false;
                    break;
                }
            }
            if (exhausted) { throw new MediaDecoderException("MEDIA_MFT_SYNC_OUTPUT_LIMIT", "同步解码输出超过单次上限"); }

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
                VideoGeometryDiagnostic allocation = new("decoder-output-allocation", _width, _height, _visibleWidth,
                    _visibleHeight, _stride, streamInfo.Size, Layout: "linear", Alignment: streamInfo.Alignment);
                int required = Nv12Geometry.Validate(allocation, requireBytes: false);
                (int outputLength, int alignmentMask) = Nv12Geometry.Allocation(required, streamInfo.Size, streamInfo.Alignment, allocation);
                suppliedBuffer = MFCreateAlignedMemoryBuffer(outputLength, alignmentMask);
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
                SelectNv12OutputType(_transform);
                return null;
            }

            result.CheckError();
            IMFSample decodedSample = output.Sample ?? throw new MediaDecoderException(
                MediaReasonCodes.H264OutputMissing,
                "Windows H.264 解码器没有返回视频帧");
            // Stamp before either TakeOutput or the return-value path can transfer
            // ownership. Async ProcessOutput is serialized by _nativeGate; the
            // synchronous decoder has one input owner. PTS need not be monotonic.
            long sequence = checked(++_outputSequence);
            DecodedVideoFrame frame = CopyDecodedFrame(decodedSample);
            frame.OutputSequence = sequence;
            return frame;
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

    private DecodedVideoFrame CopyDecodedFrame(IMFSample sample)
    {
        // Preserve a single 2-D buffer's native interface instead of asking it to
        // manufacture a different contiguous layout before inspecting its pitch.
        try
        {
            using IMFMediaBuffer buffer = sample.BufferCount == 1 ? sample.GetBufferByIndex(0) : sample.ConvertToContiguousBuffer();
            DecodedVideoFrame frame = MediaFoundationFrameCopy.Copy(buffer, _width, _height, _stride, _visibleWidth, _visibleHeight, sample.SampleTime / 10);
            frame.Color = _color;
            return frame;
        }
        catch (Exception error) when (error is SharpGenException or COMException)
        {
            throw new MediaDecoderException("MEDIA_H264_BUFFER_ACCESS_FAILED", "无法取得解码画面缓冲", error)
            { Geometry = new("decoder-buffer-access", _width, _height, _visibleWidth, _visibleHeight, 0, 0, Layout: "unavailable", DefaultStride: _stride) };
        }
    }
}
