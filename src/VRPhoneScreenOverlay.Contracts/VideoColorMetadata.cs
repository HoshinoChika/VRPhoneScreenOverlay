namespace VRPhoneScreenOverlay.Contracts;

// Numeric values are the MF_MT_* enumeration values. Null means the decoder
// omitted the attribute; zero means it explicitly reported Unknown.
public readonly record struct VideoColorMetadata(uint? YuvMatrix, uint? NominalRange,
    uint? TransferFunction, uint? Primaries)
{
    public bool HasColorMetadata => YuvMatrix.HasValue || NominalRange.HasValue || TransferFunction.HasValue || Primaries.HasValue;
}
