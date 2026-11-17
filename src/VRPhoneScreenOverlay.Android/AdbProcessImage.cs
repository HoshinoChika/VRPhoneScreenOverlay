using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VRPhoneScreenOverlay.Android;

// Query only the executable identity: do not enumerate another process's modules.
internal static class AdbProcessImage
{
    internal static string Read(int processId)
    {
        using SafeProcessHandle handle = OpenProcess(0x1000, false, processId);
        if (handle.IsInvalid) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        char[] path = new char[32768];
        int length = path.Length;
        if (!QueryFullProcessImageName(handle, 0, path, ref length))
        { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        return new string(path, 0, length);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] path, ref int size);
}
