using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal sealed class PhoneScreenshotController : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    public PhoneScreenshotController(OpenVrScreenshotChannel channel, IAndroidConnectionService phone,
        ITranslationFrameSource capture, IAndroidImageTransfer transfer, Action shutter, Action? countdown = null)
    { _worker = Task.Run(() => RunAsync(channel, phone, capture, transfer, shutter, countdown, _stop.Token)); }

    private static async Task RunAsync(OpenVrScreenshotChannel channel, IAndroidConnectionService phone,
        ITranslationFrameSource capture, IAndroidImageTransfer transfer, Action shutter, Action? countdown, CancellationToken token)
    {
        string status = "idle";
        long resetAt = 0;
        Publish();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                bool wasAvailable = channel.Available;
                channel.Available = phone.Snapshot.IsReady;
                if (resetAt != 0 && Environment.TickCount64 >= resetAt)
                { status = "idle"; resetAt = 0; Publish(); }
                else if (status == "idle" && wasAvailable != channel.Available) { Publish(); }
                if (!channel.Take()) { continue; }
                TranslationFrame? frame = null;
#pragma warning disable CA2000 // Scoped using disposes after the event handler is detached in finally; analyzer cannot follow captured ownership.
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(token);
#pragma warning restore CA2000
                operation.CancelAfter(TimeSpan.FromSeconds(45));
                using var captureLifetime = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
                var trigger = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<TranslationFrame>? prepared = null;
                string? key = phone.Snapshot.SelectedDevice?.DeviceKey;
                void Changed(object? sender, AndroidConnectionChangedEventArgs args)
                {
                    if (!args.Snapshot.IsReady || args.Snapshot.SelectedDevice?.DeviceKey != key)
                    {
                        try { operation.Cancel(); } catch (ObjectDisposedException) { }
                    }
                }
                phone.StateChanged += Changed;
                try
                {
                    if (!phone.Snapshot.IsReady || key is null) { throw new InvalidOperationException("SCREENSHOT_PHONE_UNAVAILABLE"); }
                    operation.Token.ThrowIfCancellationRequested();
                    if (capture is IArmedTranslationFrameSource armed)
                    {
                        captureLifetime.CancelAfter(TimeSpan.FromSeconds(8));
                        prepared = armed.CaptureAsync(trigger.Task, PlayShutter, captureLifetime.Token);
                    }
                    for (int seconds = 3; seconds > 0; seconds--)
                    {
                        countdown?.Invoke();
                        status = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture); Publish();
                        await Task.Delay(1000, operation.Token).ConfigureAwait(false);
                    }
                    status = "sending"; Publish();
                    captureLifetime.CancelAfter(TimeSpan.FromSeconds(5));
                    if (prepared is not null)
                    {
                        trigger.TrySetResult();
                        frame = await prepared.ConfigureAwait(false);
                    }
                    else
                    {
                        frame = await capture.CaptureAsync(captureLifetime.Token).ConfigureAwait(false);
                        PlayShutter();
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    await transfer.SendAsync(key, frame.Bytes, operation.Token).ConfigureAwait(false);
                    status = "success";
                }
                catch (OperationCanceledException) { status = "已取消或超时，请检查手机连接"; }
                catch (Exception error)
                {
                    status = error.Message switch
                    {
                        "SCREENSHOT_PHONE_UNAVAILABLE" => "请先连接手机",
                        "SCREENSHOT_PHONE_CHANGED" => "手机连接已变化，请重试",
                        "SCREENSHOT_TRANSFER_FAILED" => "发送失败，请检查手机连接或空间",
                        "SCREENSHOT_SCAN_FAILED" => "已保存到 Pictures/VRPSO，相册刷新失败",
                        "TRANSLATION_VR_UNAVAILABLE" => "SteamVR 尚未就绪",
                        _ => "截图或发送失败，请重试",
                    };
                }
                finally
                {
                    phone.StateChanged -= Changed;
                    await captureLifetime.CancelAsync().ConfigureAwait(false);
                    if (prepared is not null && frame is null)
                    {
                        try { TranslationFrame abandoned = await prepared.ConfigureAwait(false); Array.Clear(abandoned.Bytes); }
                        catch (Exception) { /* Observe cancelled/failed preparation before releasing the operation. */ }
                    }
                    if (frame is not null) { Array.Clear(frame.Bytes); }
                    channel.Complete();
                }
                void PlayShutter()
                {
                    if (operation.IsCancellationRequested || captureLifetime.IsCancellationRequested) { return; }
                    try { shutter(); }
                    catch (Exception error) when (error is InvalidOperationException or COMException)
                    { /* An optional sound failure must not discard the screenshot. */ }
                }
                Publish();
                resetAt = Environment.TickCount64 + 2000;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { channel.Complete(); }
        void Publish() => channel.Publish(Render(status, channel.Available));
    }

    internal static byte[] Render(string status, bool available = true)
    {
        using var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Microsoft YaHei UI", 36, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var text = new SolidBrush(Color.FromArgb(245, 245, 245)))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        using (var pen = new Pen(status == "success" ? UiPalette.Success :
            status is "idle" or "sending" or "1" or "2" or "3" ? available ? Color.FromArgb(245, 245, 245) : Color.FromArgb(130, 140, 155) : UiPalette.Danger, 3))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if (status is "1" or "2" or "3") { graphics.DrawString(status, font, text, new RectangleF(0, 0, 64, 61), format); }
            else if (status == "idle")
            {
                graphics.DrawRectangle(pen, 12, 23, 40, 27);
                graphics.DrawEllipse(pen, 24, 28, 16, 16);
                graphics.DrawLines(pen, [new Point(22, 23), new Point(26, 17), new Point(38, 17), new Point(42, 23)]);
            }
            else if (status == "sending")
            {
                graphics.FillEllipse(text, 14, 29, 7, 7);
                graphics.FillEllipse(text, 29, 29, 7, 7);
                graphics.FillEllipse(text, 44, 29, 7, 7);
            }
            else if (status == "success") { graphics.DrawLines(pen, [new Point(16, 32), new Point(27, 43), new Point(49, 21)]); }
            else { graphics.DrawLine(pen, 32, 15, 32, 37); graphics.DrawEllipse(pen, 30, 46, 4, 4); }
        }
        byte[] pixels = new byte[64 * 64 * 4];
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, 64, 64), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
        finally { bitmap.UnlockBits(data); }
        for (int i = 0; i < pixels.Length; i += 4) { (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]); }
        // Match the existing VR gear/translation/clipboard shell exactly. Draw
        // glyphs separately so desktop SurfaceRaised/BorderSoft cannot tint it.
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                int i = (y * 64 + x) * 4;
                float qx = MathF.Abs(x - 31.5f) - 20, qy = MathF.Abs(y - 31.5f) - 20;
                float edge = MathF.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - 12;
                float rim = Math.Clamp(1 + edge, 0, 1);
                float ink = pixels[i + 3] / 255f;
                pixels[i] = (byte)(pixels[i] * ink + (22 + 50 * rim) * (1 - ink));
                pixels[i + 1] = (byte)(pixels[i + 1] * ink + (26 + 55 * rim) * (1 - ink));
                pixels[i + 2] = (byte)(pixels[i + 2] * ink + (33 + 60 * rim) * (1 - ink));
                pixels[i + 3] = (byte)(Math.Clamp(0.5f - edge, 0, 1) * 255);
            }
        }
        return pixels;
    }
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _worker.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
