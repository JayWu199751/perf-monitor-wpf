using System.IO;

namespace PerfMonitor.App.Diagnostics;

/// <summary>
/// 轻量文件日志：追加写 %LOCALAPPDATA%\PerfMonitorWpf\app.log。
/// 用途：启动决策与退出路径诊断（优雅退出不留崩溃转储，只能靠过程日志定位）。
/// 多实例（提权交接期间新旧两个进程）会并发追加同一文件；单条写入失败即丢弃，不影响主流程。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static string FilePath => _path ??= Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerfMonitorWpf",
        "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主流程。
        }
    }
}
