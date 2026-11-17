using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Maintenance;

internal sealed record UpdateRecoveryRecord(int Schema, string Token, string InstallDirectory,
    string OldIdentity, string NewIdentity, bool Committed = false, bool Finished = false, int? SetupFailureHResult = null, string? FailureReason = null, int? FailureHResult = null);

// A runner-directory journal survives moving the installation. The lease excludes
// concurrent apply/recovery for this transaction; recovery never deletes versions.
internal sealed class UpdateRecoveryJournal : IDisposable
{
    internal const string FileName = "update-recovery.json";
    private readonly string _path;
    private readonly FileStream _lease;
    private UpdateRecoveryRecord _record;
    internal string InstallDirectory => _record.InstallDirectory;
    internal string Token => _record.Token;
    internal string? FailureReason => _record.FailureReason;

    private UpdateRecoveryJournal(string path, FileStream lease, UpdateRecoveryRecord record)
    { _path = path; _lease = lease; _record = record; }

    internal static UpdateRecoveryJournal Create(string runnerDirectory, string installDirectory, string stageDirectory, string token)
    {
        ValidateToken(token);
        string install = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string parent = Directory.GetParent(install)?.FullName ?? throw new IOException("Invalid installation path.");
        if (!string.Equals(Path.GetFullPath(stageDirectory), TransactionPath(parent, "stage", token), StringComparison.OrdinalIgnoreCase))
        { throw new IOException("Invalid update stage path."); }
        ValidateRunner(runnerDirectory, token);
        ValidateRunnerLocation(runnerDirectory, install, token);
        ValidateNoLinks(install);
        ValidateNoLinks(stageDirectory);
        FileStream? lease = Acquire(runnerDirectory);
        try
        {
            string path = Path.Combine(runnerDirectory, FileName);
            if (File.Exists(path)) { throw new IOException("Recovery journal already exists."); }
            UpdateRecoveryRecord record = new(1, token, install, Identity(install), Identity(stageDirectory));
            UpdateRecoveryJournal journal = new(path, lease, record);
            WriteDurable(Marker(stageDirectory, token), System.Text.Encoding.UTF8.GetBytes(token));
            journal.Save();
            journal.WriteRecoveryInstructions(runnerDirectory);
            lease = null; // Ownership transferred to the journal.
            return journal;
        }
        finally { lease?.Dispose(); }
    }

    internal static UpdateRecoveryJournal Open(string runnerDirectory)
    {
        ValidateNoLinks(runnerDirectory);
        FileStream? lease = Acquire(runnerDirectory);
        try
        {
            string path = Path.Combine(runnerDirectory, FileName);
            ValidateNoLinks(path);
            using FileStream source = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] bytes = new byte[8193];
            int count = source.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > 8192) { throw new IOException("Recovery journal exceeds capacity."); }
            UpdateRecoveryRecord record = JsonSerializer.Deserialize(bytes.AsSpan(0, count), UpdateRecoveryJsonContext.Default.UpdateRecoveryRecord)
                ?? throw new IOException("Recovery journal is empty.");
            ValidateToken(record.Token);
            ValidateRunner(runnerDirectory, record.Token);
            if (record.Schema != 1 || string.IsNullOrEmpty(record.OldIdentity) || string.IsNullOrEmpty(record.NewIdentity) ||
                string.IsNullOrWhiteSpace(record.InstallDirectory) || record.OldIdentity.Length != 64 || record.NewIdentity.Length != 64 ||
                !record.OldIdentity.All(Uri.IsHexDigit) || !record.NewIdentity.All(Uri.IsHexDigit) ||
                !Path.IsPathFullyQualified(record.InstallDirectory) || Directory.GetParent(record.InstallDirectory) is null ||
                !string.Equals(record.InstallDirectory, Path.GetFullPath(record.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            { throw new IOException("Recovery journal is invalid."); }
            ValidateNoLinks(record.InstallDirectory);
            ValidateRunnerLocation(runnerDirectory, record.InstallDirectory, record.Token);
            UpdateRecoveryJournal journal = new(path, lease, record);
            lease = null; // Ownership transferred to the journal.
            return journal;
        }
        finally { lease?.Dispose(); }
    }

    internal void MarkCommitted() { _record = _record with { Committed = true }; Save(); }
    internal void Finish() { _record = _record with { Finished = true }; Save(); TryRemoveMarker(); TryRemoveInstructions(); }
    internal void FinishSetupFailure(int hresult) { _record = _record with { SetupFailureHResult = hresult }; Finish(); }
    internal void RecordFailure(Exception error) => _record = _record with
    {
        FailureReason = error is Update.UpdateException update ? update.ReasonCode : Update.UpdateReasonCodes.InstallFailed,
        FailureHResult = error.HResult
    };
    internal void MarkRolledBack() { _record = _record with { Committed = false }; Save(); }
    internal void ValidateTransaction(string install, string token)
    {
        if (!string.Equals(InstallDirectory, Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || token != Token)
        { throw new IOException("Journal does not belong to this transaction."); }
    }

    internal bool IsStable() => Directory.Exists(InstallDirectory) &&
        Identity(InstallDirectory) == (_record.Committed ? _record.NewIdentity : _record.OldIdentity) &&
        (_record.Committed || !HasMarker(InstallDirectory, Token));

    internal void Recover(Func<bool>? canMove = null)
    {
        if (_record.Finished) { TryRemoveMarker(); TryRemoveInstructions(); return; }
        string parent = Directory.GetParent(InstallDirectory)!.FullName;
        string backup = TransactionPath(parent, "backup", Token);
        string failed = TransactionPath(parent, "failed", Token);
        ValidateNoLinks(InstallDirectory); ValidateNoLinks(backup); ValidateNoLinks(failed);
        if (_record.Committed && IsStable()) { Finish(); return; }
        if (Directory.Exists(backup))
        {
            if (Identity(backup) != _record.OldIdentity) { throw new IOException("Backup identity differs from the transaction."); }
            if (Directory.Exists(InstallDirectory))
            {
                if (Identity(InstallDirectory) == _record.OldIdentity && !HasMarker(InstallDirectory, Token))
                { _record = _record with { Committed = false }; Finish(); return; }
                if (Identity(InstallDirectory) != _record.NewIdentity || !HasMarker(InstallDirectory, Token) || Directory.Exists(failed))
                { throw new IOException("Installation changed outside the interrupted transaction."); }
                if (canMove is not null && !canMove()) { throw new IOException("Application is running; recovery is deferred."); }
                MoveForRecovery(InstallDirectory, failed, canMove);
            }
            if (canMove is not null && !canMove()) { throw new IOException("Application is running; recovery is deferred."); }
            MoveForRecovery(backup, InstallDirectory, canMove);
            _record = _record with { Committed = false };
        }
        if (Directory.Exists(InstallDirectory) && Identity(InstallDirectory) == _record.OldIdentity)
        { _record = _record with { Committed = false }; }
        if (!IsStable()) { throw new IOException("No intact transaction installation can be recovered."); }
        Finish();
    }

    private static void MoveForRecovery(string source, string destination, Func<bool>? canMove) =>
        UpdateDirectoryMove.Move(source, destination, move: (from, to) =>
        {
            // A new application can start during the retry interval. Check the
            // recovery gate again before every attempt, not just before waiting.
            if (canMove is not null && !canMove()) { throw new IOException("Application is running; recovery is deferred."); }
            Directory.Move(from, to);
        });

    internal string InstructionsPath => Path.Combine(Directory.GetParent(InstallDirectory)!.FullName,
        $"VRPhoneScreenOverlay-recovery-{Token}.txt");

    private void WriteRecoveryInstructions(string runner)
    {
        string helper = Path.Combine(Path.GetFullPath(runner), AppIdentity.MaintenanceExecutableName);
        string quoted = helper.Replace("'", "''", StringComparison.Ordinal);
        string instructions = $"VRPhoneScreen Overlay 更新中断恢复\r\n事务：{Token}\r\n" +
            "若更新中断后程序无法打开，请保留本文件及旁边的隐藏备份目录。\r\n" +
            "1. 先正常退出所有 VRPhoneScreen Overlay 实例；不要强制移动或删除备份。\r\n" +
            "2. 打开 PowerShell，复制执行下面的命令（无需管理员权限）：\r\n" +
            $"& '{quoted}' --recover-update\r\n$LASTEXITCODE\r\n" +
            "返回 0：恢复检查完成，可从原位置打开程序。返回 4：尚未安全恢复，请保留文件并联系作者。\r\n" +
            "自动登录恢复只尝试一次；运行中的实例、外部文件变化或磁盘问题可能使它停止，关闭实例后可用同一命令重试。\r\n" +
            "本文件不会自动执行命令，也不会更改启动设置。正常完成后会自动清理。\r\n";
        WriteDurable(InstructionsPath, System.Text.Encoding.UTF8.GetBytes(instructions));
    }

    private void TryRemoveInstructions()
    {
        try { ValidateNoLinks(InstructionsPath); File.Delete(InstructionsPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void TryRemoveMarker()
    {
        try { if (HasMarker(InstallDirectory, Token)) { File.Delete(Marker(InstallDirectory, Token)); } }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void ValidateRunnerLocation(string runner, string install, string token)
    {
        string root = Path.GetFullPath(runner).TrimEnd(Path.DirectorySeparatorChar);
        string parent = Directory.GetParent(install)!.FullName;
        string[] versions = [install, TransactionPath(parent, "stage", token), TransactionPath(parent, "backup", token), TransactionPath(parent, "failed", token)];
        if (versions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != versions.Length || versions.Any(version =>
            string.Equals(root, version, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(version + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            version.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        { throw new IOException("Recovery runner must be outside all transaction directories."); }
    }

    private void Save() => WriteDurable(_path, JsonSerializer.SerializeToUtf8Bytes(_record, UpdateRecoveryJsonContext.Default.UpdateRecoveryRecord));

    private static string Identity(string directory)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Identify the exact old/new application and its maintenance entry point.
        // This is not a replacement for the signed package's full SHA256 check.
        foreach (string relative in new[] { AppIdentity.ExecutableName, "app/VRPhoneScreenOverlay.dll", "app/" + AppIdentity.MaintenanceExecutableName })
        {
            string path = Path.Combine(directory, relative);
            ValidateNoLinks(path);
            using FileStream stream = File.OpenRead(path);
            if (stream.Length is <= 0 or > UpdatePackageLimits.MaximumEntryBytes) { throw new IOException("Invalid recovery identity file."); }
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool HasMarker(string directory, string token)
    {
        string path = Marker(directory, token);
        ValidateNoLinks(path);
        if (!File.Exists(path)) { return false; }
        using FileStream input = File.OpenRead(path);
        Span<byte> bytes = stackalloc byte[33];
        int count = input.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        return count == 32 && System.Text.Encoding.UTF8.GetString(bytes[..count]) == token;
    }
    private static string Marker(string directory, string token) => Path.Combine(directory, "app", $"update-{token}.marker");
    private static string TransactionPath(string parent, string kind, string token) => Path.Combine(parent, $".{AppIdentity.LocalDataFolderName}-{kind}-{token}");
    private static FileStream Acquire(string runner)
    {
        string path = Path.Combine(runner, "update-recovery.lock");
        ValidateNoLinks(path);
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private static void ValidateToken(string token)
    { if (token is null || token.Length != 32 || !token.All(Uri.IsHexDigit)) { throw new IOException("Invalid recovery token."); } }
    private static void ValidateRunner(string runner, string token)
    {
        ValidateNoLinks(runner);
        if (!string.Equals(Path.GetFileName(Path.GetFullPath(runner).TrimEnd(Path.DirectorySeparatorChar)), token, StringComparison.OrdinalIgnoreCase))
        { throw new IOException("Recovery runner does not match its transaction."); }
    }
    private static void ValidateNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Recovery paths cannot contain links."); }
        }
    }
    private static void WriteDurable(string path, byte[] bytes)
    {
        if (bytes.Length > 8192) { throw new IOException("Recovery record exceeds capacity."); }
        ValidateNoLinks(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { output.Write(bytes); output.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
    public void Dispose() => _lease.Dispose();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdateRecoveryRecord))]
internal sealed partial class UpdateRecoveryJsonContext : JsonSerializerContext;
