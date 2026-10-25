using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

/// <summary>Immutable, version-addressed tools outside the replaceable installation.</summary>
internal sealed class AdbRuntimeFiles(string sourceDirectory, string? runtimeRoot = null, IAndroidConnectionLogSink? log = null,
    string? evidenceRoot = null) : IDisposable
{
    private readonly string _source = Path.GetFullPath(sourceDirectory);
    private readonly string _root = Path.GetFullPath(runtimeRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.LocalDataFolderName, "adb-runtime"));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _executable;
    private readonly AdbComponentRecovery _sourceRecovery = new("installation", log, evidenceRoot);
    private readonly AdbComponentRecovery _runtimeRecovery = new("runtime", log, evidenceRoot);

    public async ValueTask<string> ResolveAsync(CancellationToken token)
    {
        if (_executable is not null) { return _executable; }
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_executable is not null) { return _executable; }
            await _sourceRecovery.EnsureAsync(_source, token).ConfigureAwait(false);
            AssertLocalDirectory(_root);
            Directory.CreateDirectory(_root);
            string identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Join('|', AndroidResourceValidator.ExpectedHashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + ":" + p.Value)))));
            string target = Path.Combine(_root, identity);
            await using FileStream gate = await AcquireAsync(token).ConfigureAwait(false);
            if (Directory.Exists(target))
            {
                AssertLocalDirectory(target);
                await _runtimeRecovery.EnsureAsync(target, token).ConfigureAwait(false);
            }
            else
            {
                if (Directory.EnumerateDirectories(_root).Take(32).Count() >= 32)
                { throw new AndroidConnectionException("ANDROID_RUNTIME_CACHE_CAPACITY", "ADB 运行副本目录已达保留上限，请清理不再使用的旧副本"); }
                string stage = Path.Combine(_root, ".stage-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                try
                {
                    foreach (string name in AndroidResourceValidator.ExpectedHashes.Keys)
                    {
                        await using FileStream input = new(Path.Combine(_source, name), FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
                        await using FileStream output = new(Path.Combine(stage, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                        await input.CopyToAsync(output, token).ConfigureAwait(false);
                    }
                    AndroidResourceValidationResult staged = await AndroidResourceValidator.ValidateAsync(stage, token).ConfigureAwait(false);
                    if (!staged.Succeeded) { throw new AndroidConnectionException("ANDROID_RUNTIME_COPY_INVALID", "ADB 运行副本校验失败"); }
                    Directory.Move(stage, target);
                }
                finally
                {
                    if (Directory.Exists(stage))
                    {
                        foreach (string name in AndroidResourceValidator.ExpectedHashes.Keys) { File.Delete(Path.Combine(stage, name)); }
                        Directory.Delete(stage, recursive: false);
                    }
                }
            }
            _executable = Path.Combine(target, "adb.exe");
            return _executable;
        }
        finally { _gate.Release(); }
    }

    private async ValueTask<FileStream> AcquireAsync(CancellationToken token)
    {
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(_root, "prepare.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5))
            { await Task.Delay(50, token).ConfigureAwait(false); }
        }
    }

    private static void AssertLocalDirectory(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("ADB runtime directory is redirected."); }
        }
    }

    public void Dispose() => _gate.Dispose();
}
