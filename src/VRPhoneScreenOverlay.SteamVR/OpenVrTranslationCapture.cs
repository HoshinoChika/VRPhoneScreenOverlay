using System.Runtime.InteropServices;
using Valve.VR;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

/// <summary>On-demand readback on a dedicated graphics device, never the phone video context.</summary>
public sealed class OpenVrTranslationCapture : ITranslationFrameSource
{
    public Task<TranslationFrame> CaptureAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Capture(cancellationToken), cancellationToken);

    public static int RunIsolatedCapture(int captureRangePercent = 65, bool armed = false)
    {
        if (!OpenVrRuntimeAvailability.IsRunning()) { Console.Error.WriteLine("TRANSLATION_VR_UNAVAILABLE"); return 1; }
        EVRInitError error = EVRInitError.None;
        CVRSystem system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
        if (error != EVRInitError.None) { Console.Error.WriteLine("TRANSLATION_VR_UNAVAILABLE"); return 1; }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(armed ? 7 : 4));
            TranslationFrame frame = Capture(timeout.Token, system, captureRangePercent,
                armed ? () => WaitForTrigger(timeout.Token) : null,
                armed ? () => { Console.Error.Write("TRANSLATION_FRAME_CAPTURED\n"); Console.Error.Flush(); }
            : null);
            try { using Stream output = Console.OpenStandardOutput(); output.Write(frame.Bytes); }
            finally { Array.Clear(frame.Bytes); }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message is "TRANSLATION_VR_UNAVAILABLE" or "TRANSLATION_CAPTURE_FORMAT_UNSUPPORTED"
                ? exception.Message : "TRANSLATION_CAPTURE_FAILED");
            return 1;
        }
        finally { OpenVR.Shutdown(); } // Detaches this helper only; never quits the SteamVR runtime.
    }

    private static void WaitForTrigger(CancellationToken token)
    {
        using Stream input = Console.OpenStandardInput();
        byte[] command = new byte[1];
        int count = input.ReadAsync(command, token).AsTask().GetAwaiter().GetResult();
        if (count != 1 || command[0] != 1) { throw new InvalidOperationException("TRANSLATION_CAPTURE_CANCELLED"); }
    }

    private static TranslationFrame Capture(CancellationToken cancellationToken, CVRSystem? independentSystem = null, int captureRangePercent = 65, Action? beforeCopy = null, Action? captured = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ulong luid = 0;
        lock (OpenVrRuntimeHost.ApiGate)
        {
            CVRSystem? system = independentSystem ?? OpenVrRuntimeHost.CurrentSystem;
            if (system is null || !OpenVrRuntimeAvailability.IsRunning())
            { throw new InvalidOperationException("TRANSLATION_VR_UNAVAILABLE"); }
            system.GetOutputDevice(ref luid, ETextureType.DirectX, 0);
        }
        using IDXGIFactory4 factory = Vortice.DXGI.DXGI.CreateDXGIFactory1<IDXGIFactory4>();
        using IDXGIAdapter1 adapter = factory.EnumAdapterByLuid<IDXGIAdapter1>(Luid.FromInt64(unchecked((long)luid)));
        Vortice.Direct3D11.D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out ID3D11Device? device).CheckError();
        if (device is null) { throw new InvalidOperationException("TRANSLATION_CAPTURE_DEVICE_FAILED"); }
        using (device)
        using (ID3D11DeviceContext context = device.ImmediateContext)
        {
            ID3D11Texture2D staging;
            Texture2DDescription description;
            Format pixelFormat;
            lock (OpenVrRuntimeHost.ApiGate)
            {
                if (independentSystem is null && OpenVrRuntimeHost.CurrentSystem is null) { throw new InvalidOperationException("TRANSLATION_VR_UNAVAILABLE"); }
                nint borrowed = 0;
                CVRCompositor compositor = OpenVR.Compositor;
                EVRCompositorError error = compositor.GetMirrorTextureD3D11(EVREye.Eye_Left, device.NativePointer, ref borrowed);
                if (error != EVRCompositorError.None || borrowed == 0)
                { throw new InvalidOperationException("TRANSLATION_CAPTURE_FAILED"); }
                try
                {
                    // Own an extra COM reference; SteamVR still owns the original mirror view.
                    if (independentSystem is not null && cancellationToken.WaitHandle.WaitOne(250))
                    { cancellationToken.ThrowIfCancellationRequested(); }
                    Marshal.AddRef(borrowed);
                    using var view = new ID3D11ShaderResourceView(borrowed);
                    using ID3D11Resource resource = view.Resource;
                    using ID3D11Texture2D texture = resource.QueryInterface<ID3D11Texture2D>();
                    description = texture.Description;
                    pixelFormat = ResolvePixelFormat(description.Format, view.Description.Format);
                    if (description.Width > 8192 || description.Height > 8192 || description.SampleDescription.Count != 1 ||
                        pixelFormat == Format.Unknown)
                    {
                        var failure = new InvalidOperationException("TRANSLATION_CAPTURE_FORMAT_UNSUPPORTED");
                        failure.Data["CaptureFormat"] = $"{description.Format}/{view.Description.Format}; {description.Width}x{description.Height}; samples={description.SampleDescription.Count}";
                        throw failure;
                    }
                    var copy = description;
                    copy.Format = pixelFormat;
                    copy.Usage = ResourceUsage.Staging;
                    copy.BindFlags = BindFlags.None;
                    copy.CPUAccessFlags = CpuAccessFlags.Read;
                    copy.MiscFlags = ResourceOptionFlags.None;
                    staging = device.CreateTexture2D(copy);
                    try { beforeCopy?.Invoke(); cancellationToken.ThrowIfCancellationRequested(); context.CopyResource(staging, texture); context.Flush(); }
                    catch { staging.Dispose(); throw; }
                }
                finally { compositor.ReleaseMirrorTextureD3D11(borrowed); }
            }
            using (staging)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MappedSubresource mapped;
                long deadline = Environment.TickCount64 + 3000;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var mappedResult = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait, out mapped);
                    if (mappedResult.Success) { break; }
                    if (mappedResult.Code != unchecked((int)0x887a000a)) { mappedResult.CheckError(); }
                    if (Environment.TickCount64 >= deadline) { throw new TimeoutException("TRANSLATION_CAPTURE_TIMEOUT"); }
                    cancellationToken.WaitHandle.WaitOne(5);
                }
                var crop = GetCaptureRegion(checked((int)description.Width), checked((int)description.Height), captureRangePercent);
                double scale = Math.Min(1, 1600d / Math.Max(crop.Width, crop.Height));
                int width = Math.Max(1, (int)(crop.Width * scale));
                int height = Math.Max(1, (int)(crop.Height * scale));
                byte[] pixels = new byte[checked(width * height * 4)];
                byte[] sourceRow = new byte[checked((int)description.Width * 4)];
                try
                {
                    try
                    {
                        // Notify after GPU readback succeeds, before conversion/PNG work.
                        captured?.Invoke();
                        for (int row = 0; row < height; row++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int sourceY = crop.Top + (int)((long)row * crop.Height / height);
                            Marshal.Copy(mapped.DataPointer + checked((int)(sourceY * mapped.RowPitch)), sourceRow, 0, sourceRow.Length);
                            for (int column = 0; column < width; column++)
                            {
                                int sourceX = (crop.Left + (int)((long)column * crop.Width / width)) * 4;
                                int target = (row * width + column) * 4;
                                pixels[target] = sourceRow[sourceX];
                                pixels[target + 1] = sourceRow[sourceX + 1];
                                pixels[target + 2] = sourceRow[sourceX + 2];
                                pixels[target + 3] = 255;
                            }
                        }
                    }
                    finally { context.Unmap(staging, 0); Array.Clear(sourceRow); }
                    if (pixelFormat is Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb)
                    {
                        for (int index = 0; index < pixels.Length; index += 4)
                        { (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]); }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(TranslationPngEncoder.Encode(pixels, width, height), "image/png", DateTimeOffset.UtcNow);
                }
                finally { Array.Clear(pixels); }
            }
        }
    }
    // Both screenshot paths crop the original eye texture before the output size cap.
    internal static (int Left, int Top, int Width, int Height) GetCaptureRegion(int width, int height, int percent = 65)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, 30);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        int cropWidth = Math.Max(1, (int)((long)width * percent / 100));
        int cropHeight = Math.Max(1, (int)((long)height * percent / 100));
        return ((width - cropWidth) / 2, (height - cropHeight) / 2, cropWidth, cropHeight);
    }

    // A typeless resource stores bytes; its SRV supplies the interpretation of those bytes.
    internal static Format ResolvePixelFormat(Format resource, Format view) => resource switch
    {
        Format.R8G8B8A8_UNorm or Format.R8G8B8A8_UNorm_SRgb or Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb => resource,
        Format.R8G8B8A8_Typeless when view is Format.R8G8B8A8_UNorm or Format.R8G8B8A8_UNorm_SRgb => view,
        Format.B8G8R8A8_Typeless when view is Format.B8G8R8A8_UNorm or Format.B8G8R8A8_UNorm_SRgb => view,
        _ => Format.Unknown,
    };

}
