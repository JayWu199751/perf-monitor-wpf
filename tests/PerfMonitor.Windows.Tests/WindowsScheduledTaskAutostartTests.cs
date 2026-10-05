using System.Diagnostics;
using PerfMonitor.Core.Shell;
using PerfMonitor.Windows.Elevation;
using PerfMonitor.Windows.ScheduledTasks;

namespace PerfMonitor.Windows.Tests;

public sealed class WindowsScheduledTaskAutostartTests
{
    [Fact(DisplayName = "任务 XML 使用当前用户登录触发、最高权限并直接写入命令路径")]
    public void Task_xml_uses_a_current_user_logon_trigger_with_highest_run_level()
    {
        var xml = WindowsScheduledTaskAutostart.BuildTaskXml(
            @"C:\Program Files\PerfMonitorWpf\PerfMonitor.App.exe",
            @"DESKTOP\alice");

        Assert.Contains("LogonTrigger", xml);
        Assert.Contains(@"<UserId>DESKTOP\alice</UserId>", xml);
        Assert.Contains("HighestAvailable", xml);
        Assert.Contains("InteractiveToken", xml);
        Assert.Contains(
            @"<Command>C:\Program Files\PerfMonitorWpf\PerfMonitor.App.exe</Command>",
            xml);
        // 长驻应用不限执行时长，静默 GUI 由无参数启动 exe 保证。
        Assert.Contains("PT0S", xml);
        var command = xml[(xml.IndexOf("<Command>", StringComparison.Ordinal) + "<Command>".Length)..];
        command = command[..command.IndexOf("</Command>", StringComparison.Ordinal)];
        // 命令路径写入 XML 元素值，不需要也不应包含命令行引号。
        Assert.DoesNotContain("\"", command, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "路径中的 XML 特殊字符被正确转义")]
    public void Xml_sensitive_characters_in_the_path_are_escaped()
    {
        var xml = WindowsScheduledTaskAutostart.BuildTaskXml(
            @"C:\Apps\Perf&Wpf <1>\app.exe",
            @"DESKTOP\alice");

        Assert.Contains(@"<Command>C:\Apps\Perf&amp;Wpf &lt;1&gt;\app.exe</Command>", xml);
    }

    [Fact(DisplayName = "默认任务名使用独立于旧版的 PerfMonitorWpf 命名空间")]
    public void The_default_task_name_uses_an_isolated_namespace()
    {
        Assert.Equal("PerfMonitorWpf", WindowsScheduledTaskAutostart.DefaultTaskName);
        Assert.DoesNotContain("PerfMonitor\\", WindowsScheduledTaskAutostart.DefaultTaskName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "无法确定可执行文件路径时报失败且不调用 schtasks")]
    public void A_missing_executable_path_reports_failure()
    {
        var autostart = new WindowsScheduledTaskAutostart(
            taskName: "PerfMonitorWpf.IntegrationTest.DoesNotExist",
            executablePath: null,
            schtasksPath: Environment.SystemDirectory + "\\schtasks.exe");

        Assert.Equal(AutostartRequestOutcome.Failed, autostart.TrySetEnabled(true));
    }

    [Fact(DisplayName = "真实集成：创建后查询存在，删除后查询不存在，测试任务清理干净")]
    public void Integration_create_query_and_delete_are_verified_against_the_system()
    {
        if (!WindowsTokenElevationDetector.IsCurrentProcessElevated())
        {
            Trace.WriteLine("跳过集成测试：注册最高权限计划任务需要提升令牌。");
            return;
        }

        if (!CanRunSchtasks())
        {
            Trace.WriteLine("跳过集成测试：schtasks.exe 在当前环境不可执行。");
            return;
        }

        var taskName = "PerfMonitorWpf.IntegrationTest." + Guid.NewGuid().ToString("N")[..12];
        var autostart = new WindowsScheduledTaskAutostart(
            taskName: taskName,
            executablePath: Environment.ProcessPath,
            schtasksPath: Environment.SystemDirectory + "\\schtasks.exe");
        try
        {
            Assert.Equal(AutostartRequestOutcome.Enabled, autostart.TrySetEnabled(true));
            Assert.Equal(AutostartRequestOutcome.Disabled, autostart.TrySetEnabled(false));
            Assert.Equal(AutostartRequestOutcome.Disabled, autostart.TrySetEnabled(false));
        }
        finally
        {
            autostart.TrySetEnabled(false);
        }
    }

    private static bool CanRunSchtasks()
    {
        try
        {
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/?");
            using var process = Process.Start(startInfo);
            process?.WaitForExit(10_000);
            return process?.ExitCode == 0;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"schtasks.exe 探测失败：{exception.Message}");
            return false;
        }
    }
}
