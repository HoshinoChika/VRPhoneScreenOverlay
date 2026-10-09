using System.Buffers;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using VRPhoneScreenOverlay.Media;
using static Vortice.Direct3D11.D3D11;

namespace VRPhoneScreenOverlay.SteamVR;

public sealed record GpuVideoFrame(
    nint NativeTexturePointer,
    int Width,
    int Height,
    long PresentationTimeMicroseconds,
    int TextureSlot);

public interface IGpuVideoFramePresenter : IDisposable
{
    public string AdapterBackend { get; }

    public GpuVideoFrame Present(DecodedVideoFrame frame);
}

public static class GpuVideoFramePresenterFactory
{
    public static IGpuVideoFramePresenter CreateD3D11(long? adapterLuid = null) =>
        new D3D11VideoFramePresenter(adapterLuid);
}

public sealed class GpuVideoPresenterException(
    string reasonCode,
    string message,
    Exception? inner = null) : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}

internal sealed class D3D11VideoFramePresenter : IGpuVideoFramePresenter
{
    private const int _textureSlotCount = 3;
    private readonly object _gate = new();
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11VideoDevice? _videoDevice;
    private ID3D11VideoContext? _videoContext;
    private ID3D11VideoProcessorEnumerator? _enumerator;
    private ID3D11VideoProcessor? _processor;
    private ID3D11Texture2D? _inputTexture;
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

    public D3D11VideoFramePresenter(long? adapterLuid)
    {
        try
        {
            (_device, string adapterName) = CreateDevice(adapterLuid);
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

        if ((frame.Width & 1) != 0 || (frame.Height & 1) != 0)
        {
            throw new GpuVideoPresenterException(
                OpenVrReasonCodes.D3d11VideoNv12OddDimensions,
                "NV12 视频宽高必须是偶数");
        }

        int requiredLength = checked(frame.Stride * frame.Height * 3 / 2);
        if (frame.Pixels.Length < requiredLength)
        {
            throw new GpuVideoPresenterException(
                OpenVrReasonCodes.D3d11VideoFrameTruncated,
                "NV12 视频帧长度不足");
        }

        lock (_gate)
        {
            try
            {
                EnsureResources(
                    frame.Width,
                    frame.Height,
                    frame.VisibleWidth,
                    frame.VisibleHeight);
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
                _videoContext!.VideoProcessorBlt(
                        _processor!,
                        _outputViews[textureSlot]!,
                        0,
                        streams)
                    .CheckError();
                _roundedMask!.Apply(_context!, textureSlot);
                _context!.Flush();

                return new GpuVideoFrame(
                    _outputTextures[textureSlot]!.NativePointer,
                    frame.VisibleWidth,
                    frame.VisibleHeight,
                    frame.PresentationTimeMicroseconds,
                    textureSlot);
            }
            catch (GpuVideoPresenterException)
            {
                throw;
            }
            catch (Exception exception) when (exception is SharpGenException or COMException)
            {
                throw new GpuVideoPresenterException(
                    OpenVrReasonCodes.D3d11VideoPresentFailed,
                    "D3D11 视频帧转换失败",
                    exception);
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
        if (_inputTexture is not null &&
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
        using MemoryHandle handle = pixels[..requiredLength].Pin();
        _context!.UpdateSubresource(
            _inputTexture!,
            0,
            null,
            (nint)handle.Pointer,
            checked((uint)stride),
            checked((uint)requiredLength));
    }

    private void ReleaseFrameResources()
    {
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
