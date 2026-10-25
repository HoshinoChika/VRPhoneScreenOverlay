using System.IO.Compression;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

public static class DiagnosticLocalFiles
{
    public static string RootDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.LocalDataFolderName);
    public static string LogsDirectory => Path.Combine(RootDirectory, "logs");
    public static string DiagnosticsDirectory => Path.Combine(RootDirectory, "diagnostics");

    public static async ValueTask<string> CreateAsync(DiagnosticUploadContext context,
        IProgress<DiagnosticProgress>? progress, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(DiagnosticsDirectory, "local");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(DiagnosticsDirectory, "资料说明.txt"),
            "local：点击“打开软件日志”时生成的最近24小时诊断包。\n" +
            "outgoing：最近6小时的上传包，上传成功或失败均保留本地副本；超限包不截断、不上传。\n" +
            "incidents：程序会话、首帧阶段、异常、退出及健康证据，时间线保留24小时。\n" +
            "component-recovery：组件校验/修复的最近证据。\n" +
            "另一个已打开的 logs 文件夹：运行日志及独立关键事件日志，按24小时窗口轮转。\n" +
            "诊断包包含运行/硬件/设置快照、时间线、错误和恢复信息；不包含手机画面、录音或完整内存。\n" +
            "当前快照和最近成功配置是背景信息，可能早于时间窗口；日志按事件时间筛选。\n",
            cancellationToken).ConfigureAwait(false);
        return await DiagnosticBundleBuilder.BuildAsync(context with { CollectionWindow = DiagnosticRetentionPolicy.LocalWindow },
            long.MaxValue, progress, cancellationToken, directory).ConfigureAwait(false);
    }

    internal static void PruneArchives(string directory)
    {
        if (!Directory.Exists(directory)) { return; }
        IncidentStorage.ValidateDirectory(directory);
        foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles("*.zip"))
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                file.LastWriteTimeUtc >= DateTime.UtcNow - DiagnosticRetentionPolicy.LocalWindow) { continue; }
            try { file.Delete(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public static async Task<string?> ReadCoverageWarningAsync(string path, CancellationToken token)
    {
        using ZipArchive archive = await ZipFile.OpenReadAsync(path, token).ConfigureAwait(false);
        return await DiagnosticCoverageReader.ReadWarningAsync(archive, token).ConfigureAwait(false);
    }
}
