using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Windows.ScheduledTasks;

/// <summary>
/// 通过 schtasks.exe 管理当前用户登录触发的最高权限计划任务（PerfMonitorWpf）。
/// 只创建、查询和删除自己的任务，绝不触碰旧版或其他自启项。
/// </summary>
public sealed class WindowsScheduledTaskAutostart : IAutostartPort
{
    public const string DefaultTaskName = "PerfMonitorWpf";

    private const int TimeoutMilliseconds = 15_000;

    private static readonly XNamespace TaskNamespace =
        "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly string _taskName;
    private readonly string? _executablePath;
    private readonly string _schtasksPath;

    public WindowsScheduledTaskAutostart(
        string? taskName = null,
        string? executablePath = null,
        string? schtasksPath = null)
    {
        _taskName = taskName ?? DefaultTaskName;
        _executablePath = executablePath ?? Environment.ProcessPath;
        _schtasksPath = schtasksPath ?? Path.Combine(Environment.SystemDirectory, "schtasks.exe");
    }

    public AutostartRequestOutcome TrySetEnabled(bool enabled)
    {
        if (string.IsNullOrWhiteSpace(_executablePath) || !File.Exists(_executablePath))
        {
            Trace.WriteLine("无法确定可执行文件路径，开机自启未更改。");
            return AutostartRequestOutcome.Failed;
        }

        try
        {
            return enabled ? Enable() : Disable();
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException)
        {
            Trace.WriteLine($"开机自启设置失败：{exception.Message}");
            return AutostartRequestOutcome.Failed;
        }
    }

    /// <summary>
    /// 构造任务定义 XML：当前用户登录触发、最高权限、交互令牌、不限执行时长；
    /// 命令路径直接写入 XML，避免命令行参数转义问题。
    /// </summary>
    public static string BuildTaskXml(string executablePath, string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(
                TaskNamespace + "Task",
                new XAttribute("version", "1.2"),
                new XElement(
                    TaskNamespace + "RegistrationInfo",
                    new XElement(
                        TaskNamespace + "Description",
                        "性能小窗（PerfMonitorWpf）当前用户登录时的自启任务。")),
                new XElement(
                    TaskNamespace + "Triggers",
                    new XElement(
                        TaskNamespace + "LogonTrigger",
                        new XElement(TaskNamespace + "Enabled", true),
                        new XElement(TaskNamespace + "UserId", userName))),
                new XElement(
                    TaskNamespace + "Principals",
                    new XElement(
                        TaskNamespace + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(TaskNamespace + "UserId", userName),
                        new XElement(TaskNamespace + "LogonType", "InteractiveToken"),
                        new XElement(TaskNamespace + "RunLevel", "HighestAvailable"))),
                new XElement(
                    TaskNamespace + "Settings",
                    new XElement(TaskNamespace + "AllowHardTerminate", true),
                    new XElement(TaskNamespace + "DisallowStartIfOnBatteries", false),
                    new XElement(TaskNamespace + "StopIfGoingOnBatteries", false),
                    new XElement(TaskNamespace + "AllowStartOnDemand", true),
                    new XElement(TaskNamespace + "Enabled", true),
                    new XElement(TaskNamespace + "Hidden", false),
                    new XElement(TaskNamespace + "RunOnlyIfIdle", false),
                    new XElement(TaskNamespace + "WakeToRun", false),
                    new XElement(TaskNamespace + "ExecutionTimeLimit", "PT0S"),
                    new XElement(TaskNamespace + "Priority", 7),
                    new XElement(TaskNamespace + "MultipleInstancesPolicy", "IgnoreNew")),
                new XElement(
                    TaskNamespace + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(
                        TaskNamespace + "Exec",
                        new XElement(TaskNamespace + "Command", executablePath)))));

        return document.Declaration + Environment.NewLine + document;
    }

    private AutostartRequestOutcome Enable()
    {
        var definitionPath = Path.Combine(
            Path.GetTempPath(),
            "PerfMonitorWpf." + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(
                definitionPath,
                BuildTaskXml(_executablePath!, CurrentUser()),
                Encoding.Unicode);
            RunSchtasks("create", "/f", "/xml", definitionPath, "/tn", _taskName);
        }
        finally
        {
            File.Delete(definitionPath);
        }

        // 创建后查询系统实际状态，失败不得报告成功。
        return TaskExists() ? AutostartRequestOutcome.Enabled : AutostartRequestOutcome.Failed;
    }

    private AutostartRequestOutcome Disable()
    {
        if (!TaskExists())
        {
            return AutostartRequestOutcome.Disabled;
        }

        RunSchtasks("delete", "/f", "/tn", _taskName);
        return TaskExists() ? AutostartRequestOutcome.Failed : AutostartRequestOutcome.Disabled;
    }

    private bool TaskExists()
    {
        return RunSchtasks("query", "/tn", _taskName) == 0;
    }

    private int RunSchtasks(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(_schtasksPath)
        {
            // 后台子进程不弹控制台窗口。
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 schtasks.exe。");
        // 先并发启动两个管道的异步读取再等待退出：若先同步读 stdout，
        // 大量 stderr 输出会填满管道使子进程写阻塞，造成双向等待死锁。
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeoutMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("schtasks.exe 执行超时。");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            Trace.WriteLine($"schtasks 返回 {process.ExitCode}：{error}{output}");
        }

        return process.ExitCode;
    }

    private static string CurrentUser() =>
        Environment.UserDomainName + "\\" + Environment.UserName;
}
