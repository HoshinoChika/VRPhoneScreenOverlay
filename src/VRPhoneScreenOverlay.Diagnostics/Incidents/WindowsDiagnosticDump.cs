using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Writes local triage dumps from an external observer, never from a fatal handler.</summary>
internal static partial class WindowsDiagnosticDump
{
    // Stack/thread/module information only; deliberately exclude full process memory.
    private const uint DumpFlags = 0x1920;

    internal static DumpCaptureQuality WriteBestEffort(Process target, FileStream destination, uint threadId, byte[]? exceptionRecord,
        long richerLimit = 32 * 1024 * 1024, long maximumBytes = 32 * 1024 * 1024)
    {
        try
        {
            Write(target, destination, threadId, exceptionRecord, Math.Min(richerLimit, maximumBytes), DumpFlags | 0x40);
            return new("stack-and-indirect-memory", null);
        }
        catch (Exception error) when (error is IOException or Win32Exception)
        {
            // Preserve a useful bounded stack/context dump if additional referenced memory cannot fit.
            destination.SetLength(0);
            destination.Position = 0;
            Write(target, destination, threadId, exceptionRecord, maximumBytes);
            return new("triage-fallback", error.HResult);
        }
    }

    internal static void Write(Process target, FileStream destination, uint threadId = 0,
        byte[]? exceptionRecord = null, long maximumBytes = 32 * 1024 * 1024, uint flags = DumpFlags)
    {
        if (target.Id == Environment.ProcessId) { throw new InvalidOperationException("External capture required."); }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using BoundedDumpIo io = new(destination, maximumBytes);
        if (exceptionRecord is null)
        {
            io.Complete(MiniDumpWriteDump(target.SafeHandle, checked((uint)target.Id), destination.SafeFileHandle,
                flags, nint.Zero, nint.Zero, io.Pointer));
            return;
        }
        if (!Environment.Is64BitProcess || exceptionRecord.Length != 152 || threadId == 0)
        { throw new ArgumentException("Expected Windows x64 exception record.", nameof(exceptionRecord)); }
        using SafeWaitHandle thread = OpenThread(0x48, false, threadId);
        if (thread.IsInvalid) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        using NativeBuffer contextStorage = new(1248);
        nint context = (contextStorage.DangerousGetHandle() + 15) & ~15;
        Marshal.WriteInt32(context, 48, 0x10000B); // CONTEXT_AMD64 | CONTEXT_FULL
        Check(GetThreadContext(thread, context));
        using NativeBuffer record = new(152);
        Marshal.Copy(exceptionRecord, 0, record.DangerousGetHandle(), exceptionRecord.Length);
        // The debug event's chained-record pointer belongs to the remote process.
        // This local record has no chain; other addresses remain target addresses.
        Marshal.WriteIntPtr(record.DangerousGetHandle(), 8, nint.Zero);
        using NativeBuffer pointers = new(16);
        Marshal.WriteIntPtr(pointers.DangerousGetHandle(), 0, record.DangerousGetHandle());
        Marshal.WriteIntPtr(pointers.DangerousGetHandle(), 8, context);
        using NativeBuffer information = new(16);
        // MINIDUMP_EXCEPTION_INFORMATION uses Windows SDK pack(4), including on x64.
        Marshal.WriteInt32(information.DangerousGetHandle(), 0, unchecked((int)threadId));
        Marshal.WriteIntPtr(information.DangerousGetHandle(), 4, pointers.DangerousGetHandle());
        Marshal.WriteInt32(information.DangerousGetHandle(), 12, 0); // ClientPointers=false
        io.Complete(MiniDumpWriteDump(target.SafeHandle, checked((uint)target.Id), destination.SafeFileHandle,
            flags, information.DangerousGetHandle(), nint.Zero, io.Pointer));
    }

    private static void Check(bool success)
    {
        if (!success) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    }

    private sealed class NativeBuffer : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal NativeBuffer(int size) : base(true)
        {
            SetHandle(Marshal.AllocHGlobal(size));
            Marshal.Copy(new byte[size], 0, handle, size);
        }

        protected override bool ReleaseHandle()
        {
            Marshal.FreeHGlobal(handle);
            return true;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeWaitHandle OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadContext(SafeWaitHandle thread, nint context);

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(SafeProcessHandle process, uint processId,
        SafeFileHandle file, uint flags, nint exceptionInformation, nint userStreams, nint callback);
}

internal sealed record DumpCaptureQuality(string Detail, int? FallbackHResult);
