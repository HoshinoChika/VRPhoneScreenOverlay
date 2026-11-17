using System.ComponentModel;
using System.Runtime.InteropServices;

namespace VRPhoneScreenOverlay.Diagnostics;

internal static partial class WindowsDiagnosticDump
{
    // DbgHelp callbacks and structures use the SDK's pack(4) ABI on Windows x64.
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool DumpCallback(nint parameter, nint input, nint output);

    internal static bool? StandardCallback(int type, nint output, bool failed)
    {
        if (type is 0 or 1 or 2 or 3 or 4 or 8 or 15) { return true; }
        if (type == 6)
        {
            Marshal.WriteInt32(output, 0, 1);
            Marshal.WriteInt32(output, 4, failed ? 1 : 0);
            return true;
        }
        if (type is 16 or 17)
        {
            Marshal.WriteInt32(output, 0); // S_OK: live process, default VM reader.
            return true;
        }
        return type is 11 or 12 or 13 ? null : false;
    }

    private sealed class BoundedDumpIo : IDisposable
    {
        private readonly NativeBuffer _information = new(16);
        private readonly DumpCallback _callback;
        private readonly byte[] _buffer = new byte[64 * 1024];
        private readonly FileStream _destination;
        private readonly long _maximumBytes;
        private Exception? _failure;
        private bool _started;

        internal BoundedDumpIo(FileStream destination, long maximumBytes)
        {
            _destination = destination;
            _maximumBytes = maximumBytes;
            _callback = Invoke;
            Marshal.WriteIntPtr(_information.DangerousGetHandle(), Marshal.GetFunctionPointerForDelegate(_callback));
        }

        internal nint Pointer => _information.DangerousGetHandle();

        internal void Complete(bool success)
        {
            int error = Marshal.GetLastPInvokeError();
            GC.KeepAlive(_callback);
            if (_failure is not null) { throw new IOException("Dump output rejected.", _failure); }
            if (!success) { throw new Win32Exception(error); }
            if (!_started) { throw new IOException("Bounded dump I/O was not activated."); }
        }

        private bool Invoke(nint parameter, nint input, nint output)
        {
            _ = parameter;
            if (input == nint.Zero || output == nint.Zero) { return false; }
            int type = Marshal.ReadInt32(input, 12);
            try
            {
                // Memory/RemoveMemory/Kernel/VM callbacks require output structures we do not supply.
                // Returning TRUE for them can make DbgHelp consume undefined union members.
                if (StandardCallback(type, output, _failure is not null) is bool handled) { return handled; }
                if (type == 11) // IoStartCallback: all writes must pass through our size check.
                {
                    _started = true;
                    Marshal.WriteInt32(output, 1); // S_FALSE
                }
                else if (type == 12) // IoWriteAllCallback
                {
                    long offset = Marshal.ReadInt64(input, 24);
                    nint source = Marshal.ReadIntPtr(input, 32);
                    uint count = unchecked((uint)Marshal.ReadInt32(input, 40));
                    if (offset < 0 || offset > _maximumBytes || count > _maximumBytes - offset)
                    { throw new IOException("Dump size limit exceeded."); }
                    for (uint done = 0; done < count;)
                    {
                        int length = (int)Math.Min((uint)_buffer.Length, count - done);
                        Marshal.Copy(source + checked((int)done), _buffer, 0, length);
                        RandomAccess.Write(_destination.SafeFileHandle, _buffer.AsSpan(0, length), offset + done);
                        done += (uint)length;
                    }
                    Marshal.WriteInt32(output, 0); // S_OK
                }
                else if (type == 13) { Marshal.WriteInt32(output, 0); } // IoFinishCallback
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverflowException)
            {
                _failure = error;
                Marshal.WriteInt32(output, unchecked((int)0x80004005)); // E_FAIL; never unwind through native code.
                return false;
            }
        }

        public void Dispose()
        {
            _information.Dispose();
            GC.KeepAlive(_callback);
        }
    }
}
