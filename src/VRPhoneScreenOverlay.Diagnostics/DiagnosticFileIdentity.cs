using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VRPhoneScreenOverlay.Diagnostics;

// File names change during rotation; creation time and length are not identities
// (Windows can preserve creation time when a name is immediately reused).
internal readonly record struct DiagnosticFileIdentity(uint Volume, uint IndexHigh, uint IndexLow)
{
    internal static DiagnosticFileIdentity Read(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out FileInformation information))
        { throw new IOException("Diagnostic file identity is unavailable.", new Win32Exception(Marshal.GetLastPInvokeError())); }
        if (((FileAttributes)information.Attributes & FileAttributes.ReparsePoint) != 0)
        { throw new IOException("Redirected diagnostic file."); }
        return new(information.VolumeSerialNumber, information.FileIndexHigh, information.FileIndexLow);
    }

    // Runtime marshalling keeps this small, read-only Windows file operation out
    // of unsafe code. The caller owns the SafeFileHandle throughout the call.
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
