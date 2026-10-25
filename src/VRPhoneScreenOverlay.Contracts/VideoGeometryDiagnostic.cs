namespace VRPhoneScreenOverlay.Contracts;

/// <summary>Buffer metadata only; never memory addresses or media contents.</summary>
public readonly record struct VideoGeometryDiagnostic(string Stage, long BufferWidth, long BufferHeight,
    long VisibleWidth, long VisibleHeight, long Stride, long Bytes, long RequiredBytes = 0,
    string Layout = "unknown", long? DefaultStride = null, long? OutputStride = null, long? Alignment = null);

public interface IVideoGeometryFailure
{
    public VideoGeometryDiagnostic? Geometry { get; }
}
