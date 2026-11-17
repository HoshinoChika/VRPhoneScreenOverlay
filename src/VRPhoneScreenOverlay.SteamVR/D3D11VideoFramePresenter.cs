using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Media;
using static Vortice.Direct3D11.D3D11;

namespace VRPhoneScreenOverlay.SteamVR;

public sealed record GpuVideoFrame(
    nint NativeTexturePointer,
    int Width,
    int Height,
    long PresentationTimeMicroseconds,
    int TextureSlot)
{
    public VideoGeometryDiagnostic? Geometry { get; init; }
}

public interface IGpuVideoFramePresenter : IDisposable
{
    public string AdapterBackend { get; }
    public string DiagnosticStage => "unknown";
    public VideoUploadDiagnostic? UploadDiagnostic => null;

    public GpuVideoFrame Present(DecodedVideoFrame frame);
}

public static class GpuVideoFramePresenterFactory
{
    public static IGpuVideoFramePresenter CreateD3D11(long? adapterLuid = null, VideoUploadSelection? selection = null) =>
        new D3D11VideoFramePresenter(adapterLuid, selection: selection);
}

public sealed class GpuVideoPresenterException(
    string reasonCode,
    string message,
    Exception? inner = null) : Exception(message, inner), IVideoGeometryFailure
{
    public string ReasonCode { get; } = reasonCode;
    public VideoGeometryDiagnostic? Geometry { get; init; }
}

internal sealed class D3D11VideoFramePresenter : IGpuVideoFramePresenter
{
    private VideoUploadDiagnostic _uploadDiagnostic;
    public VideoUploadDiagnostic UploadDiagnostic => Volatile.Read(ref _uploadDiagnostic);
    private readonly VideoUploadSelection _selection;
    private bool _directUpload;
    private bool _presented;
    internal static bool CanFallback(string requested, bool presented, int fallbackCount, int hresult) =>
        requested == "auto" && !presented && fallbackCount == 0 &&
        hresult is unchecked((int)0x80070057) or unchecked((int)0x887A0004);

    private bool TryFallback(Exception error, string stage)
    {
        if (_directUpload || !CanFallback(_selection.RequestedMode, _presented, UploadDiagnostic.FallbackCount, error.HResult)) { return false; }
        _uploadTexture?.Dispose();
        _uploadTexture = null;
        _directUpload = true;
        Volatile.Write(ref _uploadDiagnostic, UploadDiagnostic with
        {
            ActualPath = "direct",
            SelectionReason = "staging-initialization-unsupported",
            FallbackCount = 1,
            FailureStage = stage,
            FailureHResult = error.HResult
        });
        return true;
    }

    private string _diagnosticStage = "not-started";
    public string DiagnosticStage => Volatile.Read(ref _diagnosticStage);
    private string SetStage(string value) { Volatile.Write(ref _diagnosticStage, value); return value; }
    private const int _textureSlotCount = 3;
    private readonly object _gate = new();
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11VideoDevice? _videoDevice;
    private ID3D11VideoContext? _videoContext;
    private ID3D11VideoProcessorEnumerator? _enumerator;
    private ID3D11VideoProcessor? _processor;
    private ID3D11Texture2D? _inputTexture;
    private ID3D11Texture2D? _uploadTexture;
    private ID3D11VideoProcessorInputView? _inputView;
    private readonly ID3D11Texture2D?[] _outputTextures = new ID3D11Texture2D?[_textureSlotCount];
    private readonly ID3D11VideoProcessorOutputView?[] _outputViews =
        new ID3D11VideoProcessorOutputView?[_textureSlotCount];
    private D3D11RoundedPhoneMask? _roundedMask;
    private int _inputWidth;
    private int _inputHeight;
    private int _outputWidth;
    private int _outputHeight;
    private int _nextTextureSlot;
    private bool _disposed;
    private bool _resourcesReady;
    private VideoColorMetadata? _appliedColor;
    private readonly Action<string>? _resourceCheckpoint;
    private readonly Func<Format, VideoProcessorFormatSupport, VideoProcessorFormatSupport>? _formatSupportFilter;

    public D3D11VideoFramePresenter(long? adapterLuid, Action<string>? resourceCheckpoint = null,
        Func<Format, VideoProcessorFormatSupport, VideoProcessorFormatSupport>? formatSupportFilter = null, VideoUploadSelection? selection = null)
    {
        _selection = selection ?? VideoUploadSelection.Resolve("auto", null);
        if (!_selection.IsValid) { throw new GpuVideoPresenterException("D3D11_UPLOAD_OVERRIDE_INVALID", "显卡画面处理方式无效，请检查诊断启动参数或环境变量"); }
        _directUpload = _selection.RequestedMode == "direct";
        _uploadDiagnostic = new(_selection.SavedMode, _selection.RequestedMode, _selection.Source,
            _directUpload ? "direct" : "staging", _selection.RequestedMode == "auto" ? "auto-staging-default" : "forced");
        _resourceCheckpoint = resourceCheckpoint;
        _formatSupportFilter = formatSupportFilter;
        try
        {
            (_device, string adapterName) = CreateDevice(adapterLuid);
            using (IDXGIDevice dxgi = _device.QueryInterface<IDXGIDevice>())
            using (IDXGIAdapter adapter = dxgi.GetAdapter())
            {
                var description = adapter.Description;
                Result versionResult = adapter.CheckInterfaceSupport(typeof(IDXGIDevice).GUID, out long version);
                string? driver = versionResult.Success ? $"{(ulong)version >> 48}.{((ulong)version >> 32) & 65535}.{((ulong)version >> 16) & 65535}.{(ulong)version & 65535}" : null;
                _uploadDiagnostic = _uploadDiagnostic with
                {
                    VendorId = description.VendorId,
                    DeviceId = description.DeviceId,
                    DriverVersion = driver,
                    DriverVersionSource = driver is null ? "unavailable" : "dxgi-adapter-umd"
                };
            }
            _context = _device.ImmediateContext;
            _videoDevice = _device.QueryInterface<ID3D11VideoDevice>();
            _videoContext = _context.QueryInterface<ID3D11VideoContext>();
            AdapterBackend = $"{adapterName}; D3D11 feature level {_device.FeatureLevel}";
        }
        catch (Exception exception) when (exception is SharpGenException or COMException)
        {
            Dispose();
            throw new GpuVideoPresenterException(
                OpenVrReasonCodes.D3d11VideoInitializationFailed,
                "D3D11 视频设备初始化失败",
                exception);
        }
    }

    public string AdapterBackend { get; }

    public GpuVideoFrame Present(DecodedVideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame.PixelFormat != VideoPixelFormat.Nv12)
        {
            throw new GpuVideoPresenterException(
                OpenVrReasonCodes.D3d11VideoPixelFormatUnsupported,
                $"D3D11 视频转换器不支持 {frame.PixelFormat} 输入");
        }

        VideoGeometryDiagnostic geometry = new("gpu-validate", frame.Width, frame.Height, frame.VisibleWidth,
            frame.VisibleHeight, frame.Stride, frame.Pixels.Length, Layout: "nv12-to-rgba")
        { Color = frame.Color };
        int requiredLength;
        try { requiredLength = Nv12Geometry.Validate(geometry); }
        catch (MediaDecoderException error)
        {
            string reason = error.ReasonCode switch
            {
                "MEDIA_NV12_DIMENSIONS_ODD" => OpenVrReasonCodes.D3d11VideoNv12OddDimensions,
                "MEDIA_NV12_FRAME_TRUNCATED" => OpenVrReasonCodes.D3d11VideoFrameTruncated,
                _ => "D3D11_VIDEO_GEOMETRY_INVALID",
            };
            throw new GpuVideoPresenterException(reason, "视频帧布局无效，已停止提交", error) { Geometry = error.Geometry };
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string stage = SetStage("gpu-resources");
            try
            {
                EnsureResources(
                    frame.Width,
                    frame.Height,
                    frame.VisibleWidth,
                    frame.VisibleHeight);
                stage = SetStage("gpu-color");
                if (_appliedColor != frame.Color)
                {
                    VideoProcessorColorSpace inputColor = D3D11VideoColorPolicy.Input(frame.Color);
                    _videoContext!.VideoProcessorSetStreamColorSpace(_processor!, 0, inputColor);
                    _videoContext.VideoProcessorSetOutputColorSpace(_processor!, new VideoProcessorColorSpace { RGB_Range = 0, Nominal_Range = (uint)VideoProcessorNominalRange.Range_0_255 });
                    _appliedColor = frame.Color;
                }
                stage = SetStage("gpu-upload");
                UploadNv12(frame.Pixels, frame.Stride, requiredLength);

                int textureSlot = _nextTextureSlot;
                _nextTextureSlot = (_nextTextureSlot + 1) % _textureSlotCount;
                VideoProcessorStream[] streams =
                [
                    new()
                    {
                        Enable = true,
                        InputFrameOrField = 0,
                        InputSurface = _inputView,
                        OutputIndex = 0,
                    },
                ];
                stage = SetStage("gpu-convert");
                _videoContext!.VideoProcessorBlt(
                        _processor!,
                        _outputViews[textureSlot]!,
                        0,
                        streams)
                    .CheckError();
                stage = SetStage("gpu-mask");
                _roundedMask!.Apply(_context!, textureSlot);
                stage = SetStage("gpu-flush");
                _context!.Flush();

                _presented = true;
                return new GpuVideoFrame(
                    _outputTextures[textureSlot]!.NativePointer,
                    frame.VisibleWidth,
                    frame.VisibleHeight,
                    frame.PresentationTimeMicroseconds,
                    textureSlot)
                { Geometry = geometry with { Stage = "gpu-present", RequiredBytes = requiredLength, Upload = UploadDiagnostic } };
            }
            catch (GpuVideoPresenterException exception)
            {
                Volatile.Write(ref _uploadDiagnostic, UploadDiagnostic with
                {
                    FailureStage = DiagnosticStage,
                    FailureHResult = exception.InnerException?.HResult ?? exception.HResult
                });
                throw new GpuVideoPresenterException(exception.ReasonCode, exception.Message, exception)
                { Geometry = exception.Geometry ?? geometry with { Stage = DiagnosticStage, RequiredBytes = requiredLength, Upload = UploadDiagnostic } };
            }
            catch (Exception exception) when (exception is SharpGenException or COMException)
            {
                Volatile.Write(ref _uploadDiagnostic, UploadDiagnostic with { FailureStage = DiagnosticStage, FailureHResult = exception.HResult });
                throw new GpuVideoPresenterException(
                    OpenVrReasonCodes.D3d11VideoPresentFailed,
                    "D3D11 视频帧转换失败",
                    exception)
                { Geometry = geometry with { Stage = DiagnosticStage, RequiredBytes = requiredLength, Upload = UploadDiagnostic } };
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ReleaseFrameResources();
            _videoContext?.Dispose();
            _videoContext = null;
            _videoDevice?.Dispose();
            _videoDevice = null;
            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
        }
    }

    private void EnsureResources(
        int inputWidth,
        int inputHeight,
        int outputWidth,
        int outputHeight)
    {
        if (_resourcesReady && _inputTexture is not null &&
            _inputWidth == inputWidth &&
            _inputHeight == inputHeight &&
            _outputWidth == outputWidth &&
            _outputHeight == outputHeight)
        {
            return;
        }

        ReleaseFrameResources();
        _inputWidth = inputWidth;
        _inputHeight = inputHeight;
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;
        _nextTextureSlot = 0;

        VideoProcessorContentDescription content = new()
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            InputWidth = checked((uint)inputWidth),
            InputHeight = checked((uint)inputHeight),
            OutputFrameRate = new Rational(60, 1),
            OutputWidth = checked((uint)outputWidth),
            OutputHeight = checked((uint)outputHeight),
            Usage = VideoUsage.OptimalSpeed,
        };
        _enumerator = _videoDevice!.CreateVideoProcessorEnumerator(content);
        try
        {
            RequireFormatSupport(Format.NV12, VideoProcessorFormatSupport.Input, "gpu-input-format");
            RequireFormatSupport(Format.R8G8B8A8_UNorm, VideoProcessorFormatSupport.Output, "gpu-output-format");
        }
        catch
        {
            // A rejected format must not retain a partially initialized enumerator.
            ReleaseFrameResources();
            throw;
        }
        SetStage("gpu-resources");
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);

        Texture2DDescription inputDescription = new(
            Format.NV12,
            checked((uint)inputWidth),
            checked((uint)inputHeight),
            1,
            1,
            BindFlags.None,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            1,
            0,
            ResourceOptionFlags.None);
        _inputTexture = _device!.CreateTexture2D(inputDescription);
        _resourceCheckpoint?.Invoke("input-texture-created");
        if (!_directUpload)
        {
            inputDescription.Usage = ResourceUsage.Staging;
            inputDescription.CPUAccessFlags = CpuAccessFlags.Write;
            SetStage("gpu-upload-create");
            try
            {
                _resourceCheckpoint?.Invoke("upload-texture-creating");
                _uploadTexture = _device.CreateTexture2D(inputDescription);
                _resourceCheckpoint?.Invoke("upload-texture-created");
            }
            catch (Exception error) when (error is SharpGenException or COMException)
            { if (!TryFallback(error, "gpu-upload-create")) { throw; } }
            SetStage("gpu-resources");
        }
        VideoProcessorInputViewDescription inputViewDescription = new()
        {
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
        };
        _inputView = _videoDevice.CreateVideoProcessorInputView(
            _inputTexture,
            _enumerator,
            inputViewDescription);

        Texture2DDescription outputDescription = new(
            Format.R8G8B8A8_UNorm,
            checked((uint)outputWidth),
            checked((uint)outputHeight),
            1,
            1,
            BindFlags.RenderTarget | BindFlags.ShaderResource,
            ResourceUsage.Default,
            CpuAccessFlags.None,
            1,
            0,
            ResourceOptionFlags.None);
        VideoProcessorOutputViewDescription outputViewDescription = new()
        {
            ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
        };
        for (int slot = 0; slot < _textureSlotCount; slot++)
        {
            _outputTextures[slot] = _device.CreateTexture2D(outputDescription);
            _outputViews[slot] = _videoDevice.CreateVideoProcessorOutputView(
                _outputTextures[slot]!,
                _enumerator,
                outputViewDescription);
        }

        _roundedMask = new D3D11RoundedPhoneMask(_device, _outputTextures, outputWidth, outputHeight);

        _videoContext!.VideoProcessorSetStreamFrameFormat(_processor, 0, VideoFrameFormat.Progressive);
        _videoContext.VideoProcessorSetStreamAutoProcessingMode(_processor, 0, false);
        _videoContext.VideoProcessorSetStreamSourceRect(
            _processor,
            0,
            true,
            new RawRect(0, 0, outputWidth, outputHeight));
        _videoContext.VideoProcessorSetStreamDestRect(
            _processor,
            0,
            true,
            new RawRect(0, 0, outputWidth, outputHeight));
        _videoContext.VideoProcessorSetOutputTargetRect(
            _processor,
            true,
            new RawRect(0, 0, outputWidth, outputHeight));
        _resourcesReady = true;
    }

    private void RequireFormatSupport(Format format, VideoProcessorFormatSupport required, string stage)
    {
        SetStage(stage);
        VideoGeometryDiagnostic geometry = new(stage, _inputWidth, _inputHeight, _outputWidth, _outputHeight,
            0, 0, Layout: format.ToString());
        VideoProcessorFormatSupport support;
        try { support = _enumerator!.CheckVideoProcessorFormat(format); }
        catch (Exception error) when (error is SharpGenException or COMException)
        {
            throw new GpuVideoPresenterException(OpenVrReasonCodes.D3d11VideoPresentFailed,
                "无法查询显卡视频格式支持情况", error)
            { Geometry = geometry };
        }
        support = _formatSupportFilter?.Invoke(format, support) ?? support;
        if ((support & required) != required)
        {
            throw new GpuVideoPresenterException(OpenVrReasonCodes.D3d11VideoPixelFormatUnsupported,
                "显卡视频处理器不支持所需的输入或输出格式")
            { Geometry = geometry with { Layout = $"{format}; support={(int)support}; required={(int)required}" } };
        }
    }

    private static (ID3D11Device Device, string AdapterName) CreateDevice(long? adapterLuid)
    {
        DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport;
        FeatureLevel[] featureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0];
        if (adapterLuid is null)
        {
            return (
                D3D11CreateDevice(DriverType.Hardware, flags, featureLevels),
                "Default hardware adapter");
        }

        using IDXGIFactory4 factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<IDXGIFactory4>();
        using IDXGIAdapter1 adapter = factory.EnumAdapterByLuid<IDXGIAdapter1>(
            Luid.FromInt64(adapterLuid.Value));
        string adapterName = adapter.Description1.Description;
        D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                flags,
                featureLevels,
                out ID3D11Device? device)
            .CheckError();
        return (
            device ?? throw new GpuVideoPresenterException(
                OpenVrReasonCodes.D3d11SteamvrAdapterMissing,
                "无法在 SteamVR 使用的显卡上创建 D3D11 设备"),
            adapterName);
    }

    private unsafe void UploadNv12(ReadOnlyMemory<byte> pixels, int stride, int requiredLength)
    {
        if (_directUpload) { UploadDirect(pixels, stride, requiredLength); return; }
        SetStage("gpu-upload-map");
        MappedSubresource mapped;
        try
        {
            _resourceCheckpoint?.Invoke("upload-map-started");
            mapped = _context!.Map(_uploadTexture!, 0, MapMode.Write, Vortice.Direct3D11.MapFlags.None);
        }
        catch (Exception error) when (error is SharpGenException or COMException)
        {
            if (!TryFallback(error, "gpu-upload-map")) { throw; }
            UploadDirect(pixels, stride, requiredLength);
            return;
        }
        try
        {
            SetStage("gpu-upload-copy-rows");
            long mappedLength = (long)mapped.RowPitch * (_inputHeight + _inputHeight / 2);
            if (mapped.DataPointer == nint.Zero || mapped.RowPitch < _inputWidth || mappedLength > Nv12Geometry.MaximumFrameBytes)
            {
                throw new GpuVideoPresenterException(OpenVrReasonCodes.D3d11VideoPresentFailed,
                    "D3D11 上传纹理行距无效");
            }
            // DXGI NV12 staging layout: height Y rows, then height/2 UV rows,
            // both using the driver's RowPitch. DepthPitch is not a UV offset.
            Span<byte> destination = new((void*)mapped.DataPointer, checked((int)mappedLength));
            CopyNv12Rows(pixels.Span[..requiredLength], stride, destination,
                checked((int)mapped.RowPitch), _inputWidth, _inputHeight);
        }
        finally { _context.Unmap(_uploadTexture!, 0); }
        SetStage("gpu-upload-copy-resource");
        _context.CopyResource(_inputTexture!, _uploadTexture!);
    }

    private unsafe void UploadDirect(ReadOnlyMemory<byte> pixels, int stride, int requiredLength)
    {
        SetStage("gpu-upload-direct");
        using var pinned = pixels[..requiredLength].Pin();
        // Texture2D has no depth slices. NV12 Y/UV layout follows its row pitch and height.
        _context!.UpdateSubresource(_inputTexture!, 0, null, (nint)pinned.Pointer, checked((uint)stride), 0);
    }

    internal static void CopyNv12Rows(ReadOnlySpan<byte> source, int sourcePitch, Span<byte> destination,
        int destinationPitch, int width, int height)
    {
        VideoGeometryDiagnostic geometry = new("gpu-upload-copy-rows", width, height, width, height, sourcePitch, source.Length);
        Nv12Geometry.Validate(geometry);
        Nv12Geometry.Validate(geometry with { Stride = destinationPitch, Bytes = destination.Length });
        for (int row = 0; row < height + height / 2; row++)
        {
            source.Slice(checked(row * sourcePitch), width)
                .CopyTo(destination.Slice(checked(row * destinationPitch), width));
        }
    }

    private void ReleaseFrameResources()
    {
        _resourcesReady = false;
        _appliedColor = null;
        _roundedMask?.Dispose();
        _roundedMask = null;
        for (int slot = 0; slot < _textureSlotCount; slot++)
        {
            _outputViews[slot]?.Dispose();
            _outputViews[slot] = null;
            _outputTextures[slot]?.Dispose();
            _outputTextures[slot] = null;
        }

        _inputView?.Dispose();
        _inputView = null;
        _uploadTexture?.Dispose();
        _uploadTexture = null;
        _inputTexture?.Dispose();
        _inputTexture = null;
        _processor?.Dispose();
        _processor = null;
        _enumerator?.Dispose();
        _enumerator = null;
        _inputWidth = 0;
        _inputHeight = 0;
        _outputWidth = 0;
        _outputHeight = 0;
    }
}
