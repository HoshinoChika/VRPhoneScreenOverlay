using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

/// <summary>One bounded attempt per owner, with a durable rollback journal for the fixed tool group.</summary>
internal sealed class AdbComponentRecovery(string location, IAndroidConnectionLogSink? log = null,
    string? evidenceRoot = null, Func<Stream>? openArchive = null, Action<string>? checkpoint = null)
{
    private AndroidConnectionException? _failure;
    private readonly string _evidenceRoot = evidenceRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.LocalDataFolderName,
        "diagnostics", "component-recovery");
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string _transactionName = ".vrpso-adb-repair";

    public async ValueTask<AndroidResourceValidationResult> EnsureAsync(string directory, CancellationToken token)
    {
        if (_failure is not null) { throw _failure; }
        CancellationToken callerToken = token;
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        token = deadline.Token;
        directory = Path.GetFullPath(directory);
        string transaction = Path.Combine(directory, _transactionName);
        ComponentRecoveryEvidence? evidence = null;
        FileStream? lease = null;
        try
        {
            AssertPlainPath(directory);
            AndroidResourceValidationResult before = await AndroidResourceValidator.ValidateAsync(directory, token).ConfigureAwait(false);
            if (before.Succeeded && !Directory.Exists(transaction)) { return before; }
            evidence = new(1, Guid.NewGuid().ToString("N"),
                typeof(AdbComponentRecovery).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
                location, DateTimeOffset.UtcNow, "before-recovery", before.ReasonCode, before.Files);
            Directory.CreateDirectory(directory);
            lease = await AcquireAsync(Path.Combine(directory, ".vrpso-adb-repair.lock"), token).ConfigureAwait(false);
            await PersistAsync(evidence, token).ConfigureAwait(false);
            AssertPlainPath(transaction);
            if (Directory.Exists(transaction))
            {
                if (File.Exists(Path.Combine(transaction, "committed")))
                { Cleanup(transaction); }
                else
                {
                    AssertNotRunning(Path.Combine(directory, "adb.exe"));
                    await RollbackAsync(directory, transaction, token).ConfigureAwait(false);
                    Cleanup(transaction);
                }
            }
            before = await AndroidResourceValidator.ValidateAsync(directory, token).ConfigureAwait(false);
            if (before.Succeeded)
            {
                await PersistAsync(evidence with { Phase = "complete", ReasonCode = "ANDROID_RESOURCE_RECOVERED" }, token).ConfigureAwait(false);
                return before;
            }
            evidence = evidence with { Files = before.Files };
            await PersistAsync(evidence, token).ConfigureAwait(false);
            if (before.Files.Any(f => f.Status is not ("valid" or "missing" or "hash-mismatch")))
            { throw new IOException("Component cannot be safely inspected.", before.Files.First(f => f.Status is not ("valid" or "missing" or "hash-mismatch")).HResult ?? unchecked((int)0x80004005)); }
            AssertNotRunning(Path.Combine(directory, "adb.exe"));
            // Check all existing files before changing any. Real filesystem operations
            // can still fail after this preflight; the journal handles that race.
            foreach (ComponentFileEvidence file in before.Files.Where(f => f.Status != "missing"))
            { using FileStream exclusive = new(Path.Combine(directory, file.File), FileMode.Open, FileAccess.Read, FileShare.None); }
            Directory.CreateDirectory(transaction);
            string staged = Path.Combine(transaction, "new");
            Directory.CreateDirectory(staged);
            Directory.CreateDirectory(Path.Combine(transaction, "old"));
            evidence = evidence with { Phase = "extract" };
            await ExtractAsync(staged, token).ConfigureAwait(false);
            AndroidResourceValidationResult candidate = await AndroidResourceValidator.ValidateAsync(staged, token).ConfigureAwait(false);
            if (!candidate.Succeeded) { throw new InvalidDataException("Recovery source hash mismatch."); }
            // Publish the original inventory durably before the first rename.
            await WriteDurableAsync(Path.Combine(transaction, "journal.json"), before.Files, token).ConfigureAwait(false);
            evidence = evidence with { Phase = "replace" };
            await PersistAsync(evidence, token).ConfigureAwait(false);
            foreach (ComponentFileEvidence file in before.Files)
            {
                token.ThrowIfCancellationRequested();
                string target = Path.Combine(directory, file.File);
                if (file.Status != "missing") { File.Move(target, Path.Combine(transaction, "old", file.File)); }
                checkpoint?.Invoke("backed-up:" + file.File);
                File.Move(Path.Combine(staged, file.File), target);
                checkpoint?.Invoke("installed:" + file.File);
            }
            evidence = evidence with { Phase = "verify" };
            AndroidResourceValidationResult verified = await AndroidResourceValidator.ValidateAsync(directory, token).ConfigureAwait(false);
            if (!verified.Succeeded) { throw new InvalidDataException("Repaired group verification failed."); }
            await WriteDurableAsync(Path.Combine(transaction, "committed"), true, token).ConfigureAwait(false);
            evidence = evidence with { Phase = "committed" };
            Cleanup(transaction);
            await PersistAsync(evidence with { Phase = "complete", ReasonCode = "ANDROID_RESOURCE_RECOVERED" }, token).ConfigureAwait(false);
            return verified;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException or OperationCanceledException)
        {
            string rollback = "not-started";
            string? rollbackException = null;
            int? rollbackHResult = null;
            // Rollback has an independent finite budget; cancellation must not leave
            // a partially replaced group executable on a later startup.
            if (evidence?.Phase is "replace" or "verify")
            {
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(5));
                try { await RollbackAsync(directory, transaction, cleanup.Token).ConfigureAwait(false); Cleanup(transaction); rollback = "restored"; }
                catch (Exception secondary) when (secondary is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OperationCanceledException)
                { rollback = "pending-next-start"; rollbackException = secondary.GetType().FullName; rollbackHResult = secondary.HResult; }
            }
            string reason = error is InvalidDataException ? evidence?.Phase == "extract" ? "ANDROID_RECOVERY_SOURCE_INVALID" :
                evidence?.Phase == "verify" ? "ANDROID_RECOVERY_VERIFY_FAILED" : "ANDROID_RECOVERY_JOURNAL_INVALID" :
                error is UnauthorizedAccessException || (error.HResult & 0xffff) == 5 ? "ANDROID_RECOVERY_ACCESS_DENIED" :
                (error.HResult & 0xffff) is 32 or 33 ? "ANDROID_RECOVERY_COMPONENT_IN_USE" :
                (error.HResult & 0xffff) is 39 or 112 ? "ANDROID_RECOVERY_DISK_FULL" :
                error is OperationCanceledException ? callerToken.IsCancellationRequested ? "ANDROID_RECOVERY_CANCELLED" : "ANDROID_RECOVERY_TIMEOUT" : "ANDROID_RECOVERY_FAILED";
            if (evidence is not null)
            {
                try
                {
                    await PersistAsync(evidence with
                    {
                        ReasonCode = reason,
                        ExceptionType = error.GetType().FullName,
                        HResult = error.HResult,
                        RollbackStatus = rollback,
                        RollbackExceptionType = rollbackException,
                        RollbackHResult = rollbackHResult
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception storage) when (storage is IOException or UnauthorizedAccessException) { reason = "ANDROID_RECOVERY_EVIDENCE_FAILED"; }
            }
            if (error is OperationCanceledException && callerToken.IsCancellationRequested) { throw; }
            _failure = new AndroidConnectionException(reason, "手机连接组件恢复未完成，已停止自动重试；请退出相关 ADB 软件，检查磁盘和权限后重新启动本软件，仍失败请重新解压同版本完整包", error);
            throw _failure;
        }
        finally { if (lease is not null) { await lease.DisposeAsync().ConfigureAwait(false); } }
    }

    private async ValueTask ExtractAsync(string directory, CancellationToken token)
    {
        using Stream source = openArchive?.Invoke() ?? typeof(AdbComponentRecovery).Assembly.GetManifestResourceStream(
            "VRPhoneScreenOverlay.Android.adb-fixed.zip") ?? throw new InvalidDataException("Recovery source missing.");
        using ZipArchive archive = new(source, ZipArchiveMode.Read);
        if (archive.Entries.Count != AndroidResourceValidator.ExpectedHashes.Count) { throw new InvalidDataException("Unexpected recovery inventory."); }
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!AndroidResourceValidator.ExpectedHashes.Keys.Contains(entry.FullName, StringComparer.Ordinal) ||
                !names.Add(entry.FullName) || entry.Length > 32L * 1024 * 1024)
            { throw new InvalidDataException("Unexpected recovery entry."); }
            await using Stream input = await entry.OpenAsync(token).ConfigureAwait(false);
            await using FileStream output = new(Path.Combine(directory, entry.FullName), FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous);
            byte[] buffer = new byte[65536];
            long copied = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                copied += read;
                if (copied > entry.Length) { throw new InvalidDataException("Recovery entry exceeded declared size."); }
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            }
            if (copied != entry.Length) { throw new InvalidDataException("Incomplete recovery entry."); }
            await output.FlushAsync(token).ConfigureAwait(false);
            FlushDurably(output);
        }
    }

    private static async ValueTask RollbackAsync(string directory, string transaction, CancellationToken token)
    {
        string journal = Path.Combine(transaction, "journal.json");
        AssertPlainPath(journal);
        if (!File.Exists(journal)) { return; } // Preparation never changes originals before this file exists.
        if (new FileInfo(journal).Length > 16384) { throw new InvalidDataException("Invalid recovery journal."); }
        ComponentFileEvidence[] files = JsonSerializer.Deserialize<ComponentFileEvidence[]>(await File.ReadAllTextAsync(journal, token).ConfigureAwait(false), _json)
            ?? throw new InvalidDataException("Missing journal inventory.");
        if (files.Length != 3 || files.Any(f => f is null) || files.Select(f => f.File).Distinct(StringComparer.Ordinal).Count() != 3 ||
            files.Any(f => f is null || f.File is null || !AndroidResourceValidator.ExpectedHashes.ContainsKey(f.File) || f.Status is not ("valid" or "missing" or "hash-mismatch")))
        { throw new InvalidDataException("Invalid journal inventory."); }
        foreach (ComponentFileEvidence file in files)
        {
            string original = Path.Combine(transaction, "old", file.File);
            string current = Path.Combine(directory, file.File);
            AssertPlainPath(original); AssertPlainPath(current);
            if (File.Exists(original))
            {
                if (await HashAsync(original, token).ConfigureAwait(false) != file.ActualSha256)
                { throw new InvalidDataException("Original backup changed."); }
                if (File.Exists(current) && await HashAsync(current, token).ConfigureAwait(false) != AndroidResourceValidator.ExpectedHashes[file.File])
                { throw new IOException("Replacement changed during recovery."); }
                File.Move(original, current, overwrite: true);
            }
            else if (file.Status == "missing" && File.Exists(current))
            {
                if (await HashAsync(current, token).ConfigureAwait(false) != AndroidResourceValidator.ExpectedHashes[file.File])
                { throw new IOException("Replacement changed during recovery."); }
                File.Delete(current);
            }
            else if (file.Status != "missing" && (!File.Exists(current) || await HashAsync(current, token).ConfigureAwait(false) != file.ActualSha256))
            { throw new IOException("Original component is unavailable."); }
        }
    }

    private async ValueTask PersistAsync(ComponentRecoveryEvidence evidence, CancellationToken token)
    {
        AssertPlainPath(_evidenceRoot);
        Directory.CreateDirectory(_evidenceRoot);
        string name = location == "installation" ? "installation.json" : "runtime.json";
        await WriteDurableAsync(Path.Combine(_evidenceRoot, name), evidence, token).ConfigureAwait(false);
        log?.TryWrite(new(DateTimeOffset.UtcNow, "component_recovery", evidence.ReasonCode, "固定组件恢复状态",
            ExceptionType: evidence.ExceptionType)
        { ComponentRecovery = evidence });
    }

    private static async ValueTask WriteDurableAsync<T>(string path, T value, CancellationToken token)
    {
        AssertPlainPath(path);
        string temporary = path + ".tmp";
        AssertPlainPath(temporary);
        await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, value, _json, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            FlushDurably(stream);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static async ValueTask<string> HashAsync(string path, CancellationToken token)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (stream.Length > 32L * 1024 * 1024) { throw new InvalidDataException("Component exceeds size limit."); }
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }

    // FlushAsync drains managed buffers but has no flush-to-disk overload. The
    // journal must reach the filesystem before renames; only rare repair I/O uses this.
    private static void FlushDurably(FileStream stream) => stream.Flush(flushToDisk: true);

    internal static void AssertPlainPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Component path is redirected."); } }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void AssertNotRunning(string executable)
    {
        Process[] processes = Process.GetProcessesByName("adb");
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (!process.HasExited && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    { throw new IOException("Shared ADB component is in use.", unchecked((int)0x80070020)); }
                }
                catch (InvalidOperationException) { }
            }
        }
        finally { foreach (Process process in processes) { process.Dispose(); } }
    }

    private static async ValueTask<FileStream> AcquireAsync(string path, CancellationToken token)
    {
        long started = Stopwatch.GetTimestamp();
        AssertPlainPath(path);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(3))
            { await Task.Delay(50, token).ConfigureAwait(false); }
        }
    }

    private static void Cleanup(string transaction)
    {
        AssertPlainPath(transaction);
        foreach (string part in new[] { "new", "old" })
        {
            string directory = Path.Combine(transaction, part);
            AssertPlainPath(directory);
            if (!Directory.Exists(directory)) { continue; }
            foreach (string name in AndroidResourceValidator.ExpectedHashes.Keys)
            { string path = Path.Combine(directory, name); AssertPlainPath(path); File.Delete(path); }
            Directory.Delete(directory, recursive: false);
        }
        foreach (string name in new[] { "journal.json", "journal.json.tmp", "committed", "committed.tmp" })
        { string path = Path.Combine(transaction, name); AssertPlainPath(path); File.Delete(path); }
        Directory.Delete(transaction, recursive: false);
    }
}
