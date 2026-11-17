using System.Drawing.Imaging;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.App;

internal static class TranslationProbeImage
{
    public static TranslationFrame Create()
    {
        using var bitmap = new Bitmap(640, 240);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Arial", 48, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("VRPSO 4729", font, Brushes.Black, 60, 80);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return new(stream.ToArray(), "image/png", DateTimeOffset.UtcNow);
    }
}
