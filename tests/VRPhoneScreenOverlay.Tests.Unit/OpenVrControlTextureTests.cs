using System.Runtime.InteropServices;
using Valve.VR;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using VRPhoneScreenOverlay.SteamVR;
using static Vortice.Direct3D11.D3D11;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrControlTextureTests
{
    [Fact]
    public void RetainedPhoneFrameSurvivesProducerReleaseAndIsReplacedWithoutAQueue()
    {
        using ID3D11Device device = D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0);
        using OpenVrRetainedVideoTexture retained = new();
        using ID3D11Texture2D producer = device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, 2, 2));
        retained.Update(producer.NativePointer);
        nint pointer = retained.Pointer;
        producer.Dispose();
        Marshal.AddRef(pointer);
        using ID3D11Texture2D probe = new(pointer);
        Assert.Equal(2u, probe.Description.Width);
        using ID3D11Texture2D replacement = device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm, 4, 4));
        retained.Update(replacement.NativePointer);
        Assert.Equal(replacement.NativePointer, retained.Pointer);
        Assert.NotEqual(pointer, retained.Pointer);
        retained.Dispose();
        Assert.Equal(nint.Zero, retained.Pointer);
    }

    [Fact]
    public void HundredsOfRefreshesReuseOneTextureAndDoNotExhaustRawUploadBlocks()
    {
        using OverlayBoundary boundary = new();
        D3D11ControlTexture? gpu = null;
        int creations = 0;
        ID3D11Device? device = null;
        using OpenVrStaticSurface surface = new(boundary.Api, "test", (width, height) =>
        {
            creations++;
            device = D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0);
            gpu = new D3D11ControlTexture(device, width, height);
            return gpu;
        });
        byte[] pixels = OpenVrControlPixels.Background();
        nint firstTexture = 0;
        for (int update = 0; update < 500; update++)
        {
            pixels[0] = (byte)(update % 255);
            Assert.True(surface.SetPixels(pixels, 400, 64));
            Assert.True(surface.Show(OpenVrTransformMath.Identity(), 0.65f));
            firstTexture = firstTexture == 0 ? boundary.LastTexture : firstTexture;
            Assert.Equal(firstTexture, boundary.LastTexture);
        }
        Assert.Equal(1, creations);
        Assert.Equal(500, gpu!.SubmissionFlushes);
        Assert.Equal(0, boundary.RawBlocks);
        Assert.Equal(500, boundary.TextureSubmissions);
        Assert.NotNull(gpu);
        Assert.NotNull(device);
        using ID3D11Texture2D staging = device.CreateTexture2D(new Texture2DDescription(
            Format.R8G8B8A8_UNorm, 400, 64, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        using ID3D11DeviceContext context = device.ImmediateContext;
        context.CopyResource(staging, gpu.Texture);
        MappedSubresource mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try { Assert.Equal(pixels[0], Marshal.ReadByte(mapped.DataPointer)); }
        finally { context.Unmap(staging, 0); }
    }

    [Fact]
    public void FailedRefreshKeepsPreviousSurfaceVisibleAndBacksOffWithoutThrowing()
    {
        using OverlayBoundary boundary = new();
        using OpenVrStaticSurface surface = new(boundary.Api, "test", (width, height) =>
            new D3D11ControlTexture(D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport,
                FeatureLevel.Level_11_0), width, height));
        byte[] pixels = OpenVrControlPixels.Background();
        Assert.True(surface.SetPixels(pixels, 400, 64));
        Assert.True(surface.Show(OpenVrTransformMath.Identity(), 0.65f));
        boundary.FailTextureSubmission = true;
        Assert.False(surface.SetPixels(pixels, 400, 64));
        Assert.True(surface.Show(OpenVrTransformMath.Identity(), 0.65f));
        Assert.False(surface.CanUpdatePixels);
        Assert.True(surface.Show(OpenVrTransformMath.Identity(), 0.65f));
        Assert.Equal(1, surface.FailedUpdates);
        Assert.False(surface.SetPixels(pixels, 400, 64));
        Assert.Equal(2, boundary.TextureSubmissions);
    }

    [Fact]
    public void SubmissionFlushOccursAfterTheRuntimeQueuesItsTextureCopy()
    {
        using OverlayBoundary boundary = new();
        D3D11ControlTexture? gpu = null;
        using OpenVrStaticSurface surface = new(boundary.Api, "flush", (width, height) =>
            gpu = new D3D11ControlTexture(D3D11CreateDevice(DriverType.Warp,
                DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0), width, height));
        long seenBeforeCopy = -1;
        boundary.OnTextureSubmission = () => seenBeforeCopy = gpu!.SubmissionFlushes;
        Assert.True(surface.SetPixels(OpenVrControlPixels.Background(), 400, 64));
        Assert.Equal(0, seenBeforeCopy);
        Assert.Equal(1, gpu!.SubmissionFlushes);
        Assert.True(surface.SetPixels(OpenVrControlPixels.Background(), 400, 64));
        Assert.Equal(1, seenBeforeCopy);
        Assert.Equal(2, gpu.SubmissionFlushes);
    }

    // Only substitutes the OpenVR ABI boundary; no runtime, headset, controller,
    // phone session or physical input is started. Pixel updates use real D3D11 WARP.
    internal sealed class OverlayBoundary : IDisposable
    {
        private readonly IVROverlay _table;
        private readonly nint _pointer;
        public CVROverlay Api { get; }
        public int RawBlocks { get; private set; }
        public int TextureSubmissions { get; private set; }
        public nint LastTexture { get; private set; }
        public bool FailTextureSubmission { get; set; }
        public Action? OnTextureSubmission { get; set; }
        public Dictionary<ulong, float> Alphas { get; } = [];
        public HashSet<ulong> Visible { get; } = [];
        public List<ulong> Presented { get; } = [];
        private ulong _nextHandle;

        public OverlayBoundary()
        {
            _table = new IVROverlay
            {
                CreateOverlay = (nint key, nint name, ref ulong handle) => { handle = ++_nextHandle; Alphas[handle] = 1; return EVROverlayError.None; },
                SetOverlayInputMethod = (_, _) => EVROverlayError.None,
                SetOverlayRaw = (_, _, _, _, _) => ++RawBlocks > 201 ? EVROverlayError.RequestFailed : EVROverlayError.None,
                SetOverlayTexture = (ulong handle, ref Texture_t texture) =>
                {
                    TextureSubmissions++;
                    LastTexture = texture.handle;
                    OnTextureSubmission?.Invoke();
                    if (FailTextureSubmission) { return EVROverlayError.RequestFailed; }
                    Presented.Add(handle);
                    return EVROverlayError.None;
                },
                SetOverlayAlpha = (handle, alpha) => { Alphas[handle] = alpha; return EVROverlayError.None; },
                SetOverlayWidthInMeters = (_, _) => EVROverlayError.None,
                SetOverlayTexelAspect = (_, _) => EVROverlayError.None,
                SetOverlayTransformAbsolute = (ulong handle, ETrackingUniverseOrigin origin, ref HmdMatrix34_t transform) => EVROverlayError.None,
                ShowOverlay = handle => { Visible.Add(handle); return EVROverlayError.None; },
                HideOverlay = handle => { Visible.Remove(handle); return EVROverlayError.None; },
                ClearOverlayTexture = _ => EVROverlayError.None,
                DestroyOverlay = _ => EVROverlayError.None,
            };
            _pointer = Marshal.AllocHGlobal(Marshal.SizeOf<IVROverlay>());
            Marshal.StructureToPtr(_table, _pointer, false);
            Api = new CVROverlay(_pointer);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(_pointer);
            GC.KeepAlive(_table);
        }
    }
}
