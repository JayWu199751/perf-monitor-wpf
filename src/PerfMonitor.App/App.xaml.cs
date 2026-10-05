using PerfMonitor.App.Shell;
using PerfMonitor.Core.Shell;
using PerfMonitor.Windows.Elevation;
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

        try
        {
            StartApplication();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"性能小窗启动协调失败：{exception}");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
        _shellHost = new WpfStartupShellHost(this);
        _shellController = new StartupShellController(_shellHost);

        var decision = _shellController.Start(new StartupAccessContext(
            IsReleaseBuild: isReleaseBuild,
            IsElevated: isElevated,
            ElevationAlreadyAttempted: elevationAttempted,
            HasExistingInstance: !_singleInstance.IsPrimaryInstance,
            IsElevationHandoff: isElevationHandoff));

        switch (decision)
        {
            case StartupAccessDecision.NotifyExistingInstanceAndExit:
                if (!_singleInstance.NotifyExistingInstance())
                {
                    Trace.WriteLine("未能通知现有实例；新实例仍将退出以保持单实例。");
                }

                Shutdown();
                return;
            case StartupAccessDecision.HandOffToElevatedInstance:
                if (elevationHandoffToken is null
                    || !_singleInstance.TryTakeOverAfterElevation(elevationHandoffToken))
                {
                    Shutdown();
                    return;
                }

                StartPrimaryShell();
                return;
            case StartupAccessDecision.RejectElevationHandoff:
                Shutdown();
                return;
            case StartupAccessDecision.StartCurrentInstance:
                StartPrimaryShell();
                return;
            case StartupAccessDecision.StartCurrentInstanceAndAttemptElevation:
                StartPrimaryShell();
                TryStartElevatedInstance();
                return;
            default:
                throw new InvalidOperationException($"未知启动决策：{decision}");
        }
    }

    private void StartPrimaryShell()
    {
        _shellHost!.PerformanceBarRightClickRequested += (_, _) => _shellController!.OnPerformanceBarRightClick();
        _shellHost.PerformanceBarClosed += (_, _) => _shellController!.SelectMenuItem(ShellMenuAction.Exit);
        _shellHost.SettingsWindowCloseRequested += (_, _) => _shellController!.OnSettingsWindowClosed();
        _shellController!.Start();
        _singleInstance!.StartListening(
            () => Dispatcher.BeginInvoke(
                new Action(() => _shellController?.OnRepeatedLaunchRequested())),
            () => Dispatcher.BeginInvoke(new Action(Shutdown)));
    }

    private void TryStartElevatedInstance()
    {
        var token = Guid.NewGuid().ToString("N");
        _singleInstance!.ExpectElevationHandoff(token);
        if (!WindowsRunAsLauncher.TryLaunch(token))
        {
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
