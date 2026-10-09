using System.Runtime.InteropServices;
using Valve.VR;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace VRPhoneScreenOverlay.SteamVR;

// One device/context/texture per control surface. Ownership transfers to this
// object; updates replace pixels in place, with no frame queue or raw IPC blocks.
internal sealed class D3D11ControlTexture : IDisposable
{
    private readonly ID3D11Device _device;
    private ID3D11DeviceContext? _context;
    private ID3D11Texture2D? _texture;
    private bool _disposed;

    public D3D11ControlTexture(ID3D11Device device, int width, int height)
    {
        _device = device;
        try
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 2048);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 2048);
            Width = width;
            Height = height;
            _context = device.ImmediateContext;
            _texture = device.CreateTexture2D(new Texture2DDescription(
                Format.R8G8B8A8_UNorm, (uint)width, (uint)height, 1, 1,
                BindFlags.ShaderResource | BindFlags.RenderTarget,
                ResourceUsage.Default, CpuAccessFlags.None));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public nint NativePointer => _texture?.NativePointer ?? 0;
    internal ID3D11Texture2D Texture => _texture ?? throw new ObjectDisposedException(nameof(D3D11ControlTexture));
    internal long SubmissionFlushes { get; private set; }

    // SteamVR enqueues its shared-texture copy on this device during submission.
    // Flushing only the preceding CPU upload leaves that copy pending on static UI.
    public void FlushSubmission()
    {
        _context!.Flush();
        SubmissionFlushes++;
    }

    public static D3D11ControlTexture CreateForSteamVr(int width, int height)
    {
        ulong adapterLuid = 0;
        OpenVR.System.GetOutputDevice(ref adapterLuid, ETextureType.DirectX, 0);
        using IDXGIFactory4 factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<IDXGIFactory4>();
        using IDXGIAdapter1 adapter = factory.EnumAdapterByLuid<IDXGIAdapter1>(Luid.FromInt64(unchecked((long)adapterLuid)));
        ID3D11Device? device = null;
        try
        {
            D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out device).CheckError();
            D3D11ControlTexture texture = new(device ?? throw new InvalidOperationException("Control graphics device unavailable."), width, height);
            device = null; // Ownership transferred to the texture.
            return texture;
        }
        finally { device?.Dispose(); }
    }

    public void Update(byte[] pixels)
    {
        ObjectDisposedException.ThrowIf(_texture is null, this);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != checked(Width * Height * 4)) { throw new ArgumentException("Incorrect RGBA pixel count.", nameof(pixels)); }
        GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            _context!.UpdateSubresource(_texture!, 0, null, pinned.AddrOfPinnedObject(), (uint)(Width * 4), (uint)pixels.Length);
            _context.Flush();
        }
        finally { pinned.Free(); }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _texture?.Dispose();
        _texture = null;
        _context?.Dispose();
        _context = null;
        _device.Dispose();
    }
}
