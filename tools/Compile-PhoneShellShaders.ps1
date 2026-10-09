[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# Maintenance-only shader compilation. The application embeds bytecode and
# requires neither an SDK installation nor a runtime shader compiler package.
Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class PhoneShellShaderCompiler {
    [ComImport, Guid("8BA5FB08-5195-40E2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface Blob {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] UIntPtr GetBufferSize();
    }
    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int D3DCompile(byte[] data, UIntPtr length, string name, IntPtr defines, IntPtr include,
        string entry, string target, uint flags, uint effectFlags, out Blob code, out Blob errors);
    public static void Compile(string source, string entry, string target, string output) {
        byte[] data = File.ReadAllBytes(source);
        Blob code = null, errors = null;
        try {
            int result = D3DCompile(data, (UIntPtr)data.Length, source, IntPtr.Zero, IntPtr.Zero, entry, target, 32768, 0, out code, out errors);
            if (result < 0) throw new InvalidOperationException(errors == null ? "Shader compilation failed" : Marshal.PtrToStringAnsi(errors.GetBufferPointer()));
            byte[] bytes = new byte[(int)code.GetBufferSize()];
            Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length);
            File.WriteAllBytes(output, bytes);
        } finally {
            if (code != null) Marshal.ReleaseComObject(code);
            if (errors != null) Marshal.ReleaseComObject(errors);
        }
    }
}
"@
$shaderDirectory = Join-Path $PSScriptRoot '..\src\VRPhoneScreenOverlay.SteamVR\Resources\UI'
$source = Join-Path $shaderDirectory 'phone-rounded.hlsl'
[PhoneShellShaderCompiler]::Compile($source, 'Vertex', 'vs_4_0', (Join-Path $shaderDirectory 'phone-rounded.vs.cso'))
[PhoneShellShaderCompiler]::Compile($source, 'Pixel', 'ps_4_0', (Join-Path $shaderDirectory 'phone-rounded.ps.cso'))
