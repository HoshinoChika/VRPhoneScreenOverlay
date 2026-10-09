using System.Runtime.InteropServices;

namespace VRPhoneScreenOverlay.SteamVR;

// One retained COM reference, not a frame queue. It keeps the latest texture
// alive through hide/show and decoder geometry changes, even with no new frame.
internal sealed class OpenVrRetainedVideoTexture : IDisposable
{
    public nint Pointer { get; private set; }

    public void Update(nint pointer)
    {
        ArgumentOutOfRangeException.ThrowIfZero(pointer);
        if (Pointer == pointer) { return; }
        Marshal.AddRef(pointer);
        nint previous = Pointer;
        Pointer = pointer;
        if (previous != 0) { Marshal.Release(previous); }
    }

    public void Dispose()
    {
        nint previous = Pointer;
        Pointer = 0;
        if (previous != 0) { Marshal.Release(previous); }
    }
}
