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
    private static readonly TimeSpan ElevationTakeoverFallbackDelay = TimeSpan.FromSeconds(5);

    private WpfStartupShellHost? _shellHost;
    private StartupShellController? _shellController;
    private WindowsSingleInstanceCoordinator? _singleInstance;
    private System.Windows.Threading.DispatcherTimer? _elevationFallbackTimer;
    private bool _shellEventsWired;

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
        _elevationFallbackTimer?.Stop();
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
                TryElevateBeforeFirstPaint();
                return;
            default:
                throw new InvalidOperationException($"未知启动决策：{decision}");
        }
    }

    private void EnsureShellEventsWired()
    {
        if (_shellEventsWired)
        {
            return;
        }

        _shellEventsWired = true;
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
    }

    private void StartPrimaryShell()
    {
        EnsureShellEventsWired();
        _shellController!.Start();
        StartInstanceListening();
    }

    private void StartInstanceListening() =>
        _singleInstance!.StartListening(
            () => Dispatcher.BeginInvoke(
                new Action(() => _shellController?.OnRepeatedLaunchRequested())),
            () =>
            {
                AppLog.Write("提权实例请求接管 → 原实例退出。");
                Dispatcher.BeginInvoke(new Action(Shutdown));
            });

    /// <summary>
    /// 提权先于首帧（ADR-0001 实现说明 2026-10-06）：普通权限启动先立起 IPC 监听并拉起提权实例，
    /// 接管完成前本实例零界面，由提权实例一次性显壳，消除“出现→消失→再出现”的交接闪烁。
    /// 拒绝 UAC → 立即普通权限显壳；提权实例拉起后 5 秒未接管（异常退出等）→ 兜底普通权限显壳。
    /// 兜底不撤销交接登记：迟到的提权实例在令牌有效期内仍可接管。
    /// </summary>
    private void TryElevateBeforeFirstPaint()
    {
        // TAKEOVER 的接收方必须先于提权实例启动就位；监听幂等，后续 StartPrimaryShell 不会重复。
        EnsureShellEventsWired();
        StartInstanceListening();

        var token = Guid.NewGuid().ToString("N");
        _singleInstance!.ExpectElevationHandoff(token);
        if (!WindowsRunAsLauncher.TryLaunch(token))
        {
            AppLog.Write("提权实例未启动（用户拒绝 UAC 或启动失败），当前实例以普通权限显壳。");
            _singleInstance.ClearExpectedElevationHandoff(token);
            StartPrimaryShell();
            return;
        }

        AppLog.Write("提权实例已拉起，等待其接管显壳；5 秒未接管则本实例普通权限显壳。");
        _elevationFallbackTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = ElevationTakeoverFallbackDelay
        };
        _elevationFallbackTimer.Tick += (_, _) =>
        {
            _elevationFallbackTimer!.Stop();
            _elevationFallbackTimer = null;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            AppLog.Write("提权实例 5 秒未完成接管（可能异常退出），回退为普通权限显壳。");
            StartPrimaryShell();
        };
        _elevationFallbackTimer.Start();
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
