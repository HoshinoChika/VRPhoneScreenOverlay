using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace VRPhoneScreenOverlay.SteamVR;

// Updates alpha only in the presenter's existing three slots. No extra video
// texture, CPU readback, frame queue or per-frame managed allocation is needed.
internal sealed class D3D11RoundedPhoneMask : IDisposable
{
    private ID3D11VertexShader? _vertex;
    private ID3D11PixelShader? _pixel;
    private ID3D11Buffer? _geometry;
    private ID3D11BlendState? _alphaOnly;
    private readonly ID3D11RenderTargetView?[] _targets;
    private readonly int _width;
    private readonly int _height;

    public D3D11RoundedPhoneMask(ID3D11Device device, ID3D11Texture2D?[] textures, int width, int height)
    {
        _targets = new ID3D11RenderTargetView?[textures.Length];
        _width = width;
        _height = height;
        try
        {
            _vertex = device.CreateVertexShader(Load("PhoneRoundedVertex"));
            _pixel = device.CreatePixelShader(Load("PhoneRoundedPixel"));
            _geometry = device.CreateBuffer<Vector4>([new(width, height, Math.Min(width, height) * OpenVrPhoneShellGeometry.CornerRadius, 0)],
                BindFlags.ConstantBuffer, ResourceUsage.Immutable);
            BlendDescription blend = BlendDescription.Opaque;
            blend.RenderTarget[0].RenderTargetWriteMask = ColorWriteEnable.Alpha;
            _alphaOnly = device.CreateBlendState(blend);
            for (int i = 0; i < textures.Length; i++) { _targets[i] = device.CreateRenderTargetView(textures[i]!); }
        }
        catch { Dispose(); throw; }
    }

    public void Apply(ID3D11DeviceContext context, int slot)
    {
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertex);
        context.PSSetShader(_pixel);
        context.PSSetConstantBuffer(0, _geometry);
        context.RSSetViewport(0, 0, _width, _height);
        context.OMSetRenderTargets(_targets[slot]!);
        context.OMSetBlendState(_alphaOnly);
        context.Draw(3, 0);
        context.OMSetRenderTargets(0, Array.Empty<ID3D11RenderTargetView>(), null);
        context.OMSetBlendState(null);
    }

    private static byte[] Load(string name)
    {
        using Stream stream = typeof(D3D11RoundedPhoneMask).Assembly.GetManifestResourceStream(
            "VRPhoneScreenOverlay.SteamVR." + name) ?? throw new InvalidOperationException("Phone mask shader missing.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public void Dispose()
    {
        foreach (ID3D11RenderTargetView? target in _targets) { target?.Dispose(); }
        _alphaOnly?.Dispose(); _alphaOnly = null;
        _geometry?.Dispose(); _geometry = null;
        _pixel?.Dispose(); _pixel = null;
        _vertex?.Dispose(); _vertex = null;
    }
}
