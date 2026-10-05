using PerfMonitor.App.Diagnostics;
using PerfMonitor.App.Shell;
using PerfMonitor.Core.Shell;
using PerfMonitor.Windows.Elevation;
using PerfMonitor.Windows.Settings;
using WpfApplication = System.Windows.Application;
using System.Windows;
using System.Diagnostics;

namespace PerfMonitor.App;

public partial class App : WpfApplication
{
    private WpfStartupShellHost? _shellHost;
    private StartupShellController? _shellController;
    private WindowsSingleInstanceCoordinator? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Write($"启动 pid={Environment.ProcessId} 提权={WindowsTokenElevationDetector.IsCurrentProcessElevated()} 参数=[{string.Join(' ', e.Args)}]");

        try
        {
            StartApplication();
        }
        catch (Exception exception)
        {
            AppLog.Write($"启动协调失败：{exception}");
            Trace.WriteLine($"性能小窗启动协调失败：{exception}");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write($"退出 pid={Environment.ProcessId} code={e.ApplicationExitCode}");
        _shellController?.Dispose();
        _shellHost?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void StartApplication()
    {
        var isElevated = WindowsTokenElevationDetector.IsCurrentProcessElevated();
        var (elevationAttempted, elevationHandoffToken) = ReadElevationArguments();
        var isElevationHandoff = elevationAttempted && elevationHandoffToken is not null;

#if DEBUG
        const bool isReleaseBuild = false;
#else
        const bool isReleaseBuild = true;
#endif

        _singleInstance = WindowsSingleInstanceCoordinator.Acquire();
        _shellHost = new WpfStartupShellHost(this, new WindowsSettingsStore(WindowsSettingsStore.GetDefaultPath()));
        _shellController = new StartupShellController(_shellHost);

        var decision = _shellController.Start(new StartupAccessContext(
            IsReleaseBuild: isReleaseBuild,
            IsElevated: isElevated,
            ElevationAlreadyAttempted: elevationAttempted,
            HasExistingInstance: !_singleInstance.IsPrimaryInstance,
            IsElevationHandoff: isElevationHandoff));
        AppLog.Write($"启动决策={decision} isPrimary={_singleInstance.IsPrimaryInstance} release={isReleaseBuild}");

        switch (decision)
        {
            case StartupAccessDecision.NotifyExistingInstanceAndExit:
                if (!_singleInstance.NotifyExistingInstance())
                {
                    AppLog.Write("未能通知现有实例；新实例仍将退出以保持单实例。");
                    Trace.WriteLine("未能通知现有实例；新实例仍将退出以保持单实例。");
                }

                AppLog.Write("退出原因：已有实例在运行（NotifyExistingInstanceAndExit）");
                Shutdown();
                return;
            case StartupAccessDecision.HandOffToElevatedInstance:
                if (elevationHandoffToken is null
                    || !_singleInstance.TryTakeOverAfterElevation(elevationHandoffToken))
                {
                    AppLog.Write("提权接管失败（令牌无效或等锁超时），退出。");
                    Shutdown();
                    return;
                }

                AppLog.Write("提权接管成功，启动主壳。");
                StartPrimaryShell();
                return;
            case StartupAccessDecision.RejectElevationHandoff:
                AppLog.Write("拒绝提权交接（本实例非提权进程），退出。");
                Shutdown();
                return;
            case StartupAccessDecision.StartCurrentInstance:
                AppLog.Write("启动主壳（无提权尝试）。");
                StartPrimaryShell();
                return;
            case StartupAccessDecision.StartCurrentInstanceAndAttemptElevation:
                StartPrimaryShell();
                AppLog.Write("主壳已启动，尝试拉起提权实例。");
                TryStartElevatedInstance();
                return;
            default:
                throw new InvalidOperationException($"未知启动决策：{decision}");
        }
    }

    private void StartPrimaryShell()
    {
        _shellHost!.PerformanceBarRightClickRequested += (_, _) => _shellController!.OnPerformanceBarRightClick();
        _shellHost.PerformanceBarClosed += (_, _) =>
        {
            AppLog.Write("小窗窗口被关闭 → 走退出清理链。");
            _shellController!.SelectMenuItem(ShellMenuAction.Exit);
        };
        _shellHost.SettingsWindowCloseRequested += (_, _) => _shellController!.OnSettingsWindowClosed();
        // 系统注销/关机与共享菜单退出走同一条清理链：停止采样、flush 落位、停 watcher、撤托盘、放行窗口关闭。
        SessionEnding += (_, _) =>
        {
            AppLog.Write("系统会话结束（注销/关机）→ 走退出清理链。");
            _shellController!.SelectMenuItem(ShellMenuAction.Exit);
        };
        _shellController!.Start();
        _singleInstance!.StartListening(
            () => Dispatcher.BeginInvoke(
                new Action(() => _shellController?.OnRepeatedLaunchRequested())),
            () =>
            {
                AppLog.Write("提权实例请求接管 → 原实例退出。");
                Dispatcher.BeginInvoke(new Action(Shutdown));
            });
    }

    private void TryStartElevatedInstance()
    {
        var token = Guid.NewGuid().ToString("N");
        _singleInstance!.ExpectElevationHandoff(token);
        if (!WindowsRunAsLauncher.TryLaunch(token))
        {
            AppLog.Write("提权实例未启动（用户拒绝 UAC 或启动失败），当前实例继续以普通权限运行。");
            _singleInstance.ClearExpectedElevationHandoff(token);
        }
    }

    private static (bool ElevationAttempted, string? HandoffToken) ReadElevationArguments()
    {
        var arguments = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var elevationAttempted = arguments.Contains("--elevation-attempted", StringComparer.Ordinal);
        var handoffArgument = arguments.FirstOrDefault(argument =>
            argument.StartsWith("--elevation-handoff=", StringComparison.Ordinal));
        var token = handoffArgument?["--elevation-handoff=".Length..];
        return (elevationAttempted, Guid.TryParseExact(token, "N", out var parsed) ? parsed.ToString("N") : null);
    }
}
