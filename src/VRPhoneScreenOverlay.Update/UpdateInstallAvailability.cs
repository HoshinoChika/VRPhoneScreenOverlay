using System.ComponentModel;
using System.Diagnostics;

namespace VRPhoneScreenOverlay.Update;

public static class UpdateInstallAvailability
{
    public const string SharedAdbInUse = "UPDATE_SHARED_ADB_IN_USE";
    public const string OwnerUnavailable = "UPDATE_ADB_OWNER_UNAVAILABLE";
    public const string RecoveryMessage = "更新暂未完成，已保留原版本。ADB 文件占用未能安全排除，请退出相关 ADB 软件后重试更新。";

    public static void EnsureAvailable(string installDirectory) => EnsureAvailable(installDirectory, IsRunning);

    internal static void EnsureAvailable(string installDirectory, Func<string, bool> isRunning)
    {
        string executable = Path.Combine(Path.GetFullPath(installDirectory), "app", "resources", "android-platform-tools", "adb.exe");
        if (File.Exists(executable) && isRunning(executable))
        { throw new UpdateException(SharedAdbInUse, "旧安装目录中的 ADB 仍被使用，请先退出相关 ADB 软件再重试；共享服务已保留"); }
    }

    private static bool IsRunning(string executable)
    {
        foreach (Process process in Process.GetProcessesByName("adb"))
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited && string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) { return true; }
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception exception)
                { throw new UpdateException(OwnerUnavailable, "无法确认 ADB 进程归属，请先退出相关 ADB 软件再重试更新", exception); }
            }
        }
        return false;
    }
}
