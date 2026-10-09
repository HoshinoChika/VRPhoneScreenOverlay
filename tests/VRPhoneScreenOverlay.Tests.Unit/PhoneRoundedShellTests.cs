using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using VRPhoneScreenOverlay.SteamVR;
using static Vortice.Direct3D11.D3D11;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneRoundedShellTests
{
    [Theory]
    [InlineData(240, 480)]
    [InlineData(480, 240)]
    public void GpuMaskPreservesRgbAndDimensionsWhileMakingCornersTransparent(int width, int height)
    {
        using ID3D11Device device = D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0);
        using ID3D11DeviceContext context = device.ImmediateContext;
        using ID3D11Texture2D texture = device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm,
            (uint)width, (uint)height, 1, 1, BindFlags.RenderTarget));
        using ID3D11RenderTargetView target = device.CreateRenderTargetView(texture);
        context.ClearRenderTargetView(target, new Vortice.Mathematics.Color4(0.2f, 0.4f, 0.6f, 1));
        using D3D11RoundedPhoneMask mask = new(device, [texture], width, height);
        mask.Apply(context, 0);
        using ID3D11Texture2D staging = device.CreateTexture2D(new Texture2DDescription(Format.R8G8B8A8_UNorm,
            (uint)width, (uint)height, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        context.CopyResource(staging, texture);
        MappedSubresource mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        byte[] rgba = new byte[width * height * 4];
        try
        {
            for (int y = 0; y < height; y++) { Marshal.Copy(mapped.DataPointer + checked(y * (int)mapped.RowPitch), rgba, y * width * 4, width * 4); }
        }
        finally { context.Unmap(staging, 0); }
        Assert.Equal(0, rgba[3]);
        Assert.Equal(0, rgba[((height - 1) * width + width - 1) * 4 + 3]);
        Assert.Equal(255, rgba[(height / 2 * width + width / 2) * 4 + 3]);
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            Assert.Equal(51, rgba[pixel * 4]);
            Assert.Equal(102, rgba[pixel * 4 + 1]);
            Assert.Equal(153, rgba[pixel * 4 + 2]);
        }
        Export("screen-" + width + "x" + height, rgba, width, height);
        Export("case-" + width + "x" + height, OpenVrPhoneShellGeometry.Pixels((float)width / height),
            OpenVrPhoneShellGeometry.TextureSize, OpenVrPhoneShellGeometry.TextureSize);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(2f)]
    public void ShellGrowsOutsideTheScreenAndTransparentCornersCannotBeTouched(float aspect)
    {
        var outer = OpenVrPhoneShellGeometry.OuterSize(0.3f, aspect);
        Assert.True(outer.Width > 0.3f);
        Assert.True(outer.Width / outer.Aspect > 0.3f / aspect);
        Assert.False(OpenVrPhoneShellGeometry.ContainsScreenPoint(0, 0, aspect));
        Assert.False(OpenVrPhoneShellGeometry.ContainsScreenPoint(1, 1, aspect));
        Assert.True(OpenVrPhoneShellGeometry.ContainsScreenPoint(0.5f, 0.5f, aspect));
        Assert.True(OpenVrPhoneShellGeometry.ContainsScreenPoint(0.5f, 0, aspect));
    }

    [Fact]
    public void ShellSurfacesHideTogetherAndReuseTexturesAcrossScalingAndRotation()
    {
        using OpenVrControlTextureTests.OverlayBoundary boundary = new();
        int textures = 0;
        using OpenVrPhoneBackface shell = new(boundary.Api, (width, height) =>
        {
            textures++;
            return new D3D11ControlTexture(D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport,
                FeatureLevel.Level_11_0), width, height);
        });
        shell.Update(true, OpenVrTransformMath.Identity(), 0.3f, 0.5f);
        Assert.Equal(4, boundary.Visible.Count);
        shell.Update(true, OpenVrTransformMath.Identity(), 0.6f, 0.5f);
        Assert.Equal(4, boundary.TextureSubmissions);
        shell.Update(true, OpenVrTransformMath.Identity(), 0.6f, 2);
        Assert.Equal(4, textures);
        Assert.Equal(8, boundary.TextureSubmissions);
        shell.Update(false, OpenVrTransformMath.Identity(), 0.6f, 2);
        Assert.Empty(boundary.Visible);
    }

    [Fact]
    public void OpacityFadesBothPlatesButLeavesHollowRimsOpaqueWithoutUploadingTextures()
    {
        using OpenVrControlTextureTests.OverlayBoundary boundary = new();
        using OpenVrPhoneBackface shell = new(boundary.Api, (width, height) =>
            new D3D11ControlTexture(D3D11CreateDevice(DriverType.Warp, DeviceCreationFlags.BgraSupport,
                FeatureLevel.Level_11_0), width, height));
        foreach (float opacity in new[] { 1f, 0.5f, 0f, 0.8f, 1f })
        {
            shell.Update(true, OpenVrTransformMath.Identity(), 0.3f, 0.5f, opacity);
            Assert.Equal(new[] { opacity, opacity, 1f, 1f }.Order(), boundary.Alphas.Values.Order());
            Assert.Equal(4, boundary.TextureSubmissions);
        }
        shell.Update(false, OpenVrTransformMath.Identity(), 0.3f, 0.5f, 0.5f);
        Assert.Empty(boundary.Visible);
        shell.Update(true, OpenVrTransformMath.Identity(), 0.3f, 0.5f, 0.5f);
        Assert.Equal(2, boundary.Alphas.Values.Count(alpha => alpha == 0.5f));
        byte[] rim = OpenVrPhoneShellGeometry.Pixels(0.5f, rimOnly: true);
        int center = (256 * 512 + 256) * 4;
        Export("hollow-rim", rim, 512, 512);
        Export("fading-plate", OpenVrPhoneShellGeometry.Pixels(0.5f), 512, 512);
        Assert.Equal(0, rim[center + 3]);
        Assert.Contains(Enumerable.Range(0, 512 * 512), pixel => rim[pixel * 4 + 3] > 0);
    }

    private static void Export(string name, byte[] pixels, int width, int height)
    {
        string? directory = Environment.GetEnvironmentVariable("VRPHONE_SHELL_SNAPSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        Directory.CreateDirectory(directory);
        using Bitmap bitmap = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = (y * width + x) * 4;
                bitmap.SetPixel(x, y, Color.FromArgb(pixels[offset + 3], pixels[offset], pixels[offset + 1], pixels[offset + 2]));
            }
        }
        bitmap.Save(Path.Combine(directory, name + ".png"), ImageFormat.Png);
    }
}
