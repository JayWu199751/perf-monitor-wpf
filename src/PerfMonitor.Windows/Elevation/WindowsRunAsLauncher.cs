using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace PerfMonitor.Windows.Elevation;

public static class WindowsRunAsLauncher
{
    public static bool TryLaunch(string elevationHandoffToken)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            System.Diagnostics.Trace.WriteLine("无法确定当前可执行文件路径，跳过 runas 提权尝试。");
            return false;
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory
        };
        startInfo.ArgumentList.Add("--elevation-attempted");
        startInfo.ArgumentList.Add($"--elevation-handoff={elevationHandoffToken}");

        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Win32Exception exception)
        {
            // ERROR_CANCELLED (1223) 表示用户拒绝 UAC；当前普通实例继续运行。
            System.Diagnostics.Trace.WriteLine($"runas 提权未启动，错误码 {exception.NativeErrorCode}。");
            return false;
        }
        catch (InvalidOperationException exception)
        {
            System.Diagnostics.Trace.WriteLine($"runas 提权启动失败：{exception.Message}");
            return false;
        }
        catch (ArgumentException exception)
        {
            System.Diagnostics.Trace.WriteLine($"runas 启动参数无效：{exception.Message}");
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException)
        {
            System.Diagnostics.Trace.WriteLine($"runas 提权进程启动失败：{exception.Message}");
            return false;
        }
    }
}
