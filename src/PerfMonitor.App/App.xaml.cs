using PerfMonitor.App.Shell;
using PerfMonitor.Core.Shell;
using WpfApplication = System.Windows.Application;
using System.Windows;

namespace PerfMonitor.App;

public partial class App : WpfApplication
{
    private WpfStartupShellHost? _shellHost;
    private StartupShellController? _shellController;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _shellHost = new WpfStartupShellHost(this);
        _shellController = new StartupShellController(_shellHost);
        _shellHost.PerformanceBarRightClickRequested += (_, _) => _shellController.OnPerformanceBarRightClick();
        _shellHost.PerformanceBarClosed += (_, _) => _shellController.SelectMenuItem(ShellMenuAction.Exit);
        _shellHost.SettingsWindowCloseRequested += (_, _) => _shellController.OnSettingsWindowClosed();
        _shellController.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shellHost?.Dispose();
        base.OnExit(e);
    }
}
