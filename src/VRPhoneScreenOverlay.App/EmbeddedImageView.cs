using System.ComponentModel;

namespace VRPhoneScreenOverlay.App;

public sealed class EmbeddedImageView : Control
{
    private Bitmap? _image;
    private string _resourceName = string.Empty;

    public EmbeddedImageView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    [DefaultValue("")]
    public string ResourceName
    {
        get => _resourceName;
        set
        {
            if (value == _resourceName) { return; }
            using Stream? stream = typeof(EmbeddedImageView).Assembly.GetManifestResourceStream(
                "VRPhoneScreenOverlay.App.Assets." + value);
            Bitmap? next = null;
            if (stream is not null)
            {
                using Bitmap source = new(stream);
                next = new Bitmap(source);
            }
            _image?.Dispose();
            _image = next;
            _resourceName = value;
            Invalidate();
        }
    }

    [DefaultValue(typeof(Rectangle), "0, 0, 0, 0")]
    public Rectangle Crop { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image is null) { return; }
        Rectangle source = Crop.IsEmpty ? new(0, 0, _image.Width, _image.Height) : Crop;
        e.Graphics.DrawImage(_image, ClientRectangle, source, GraphicsUnit.Pixel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _image?.Dispose(); }
        base.Dispose(disposing);
    }
}
