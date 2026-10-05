using PerfMonitor.Core.Shell;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Core.Tests;

public sealed class StartupShellContractTests
{
    [Fact(DisplayName = "启动后只显示一个性能条和托盘，不抢焦点，也不创建设置窗")]
    public void Startup_shows_one_performance_bar_and_tray_without_activating_or_opening_settings()
    {
        var host = new RecordingStartupShellHost();
        var shell = new StartupShellController(host);

        shell.Start();

        Assert.True(shell.State.IsRunning);
        Assert.True(shell.State.IsPerformanceBarVisible);
        Assert.True(shell.State.IsTrayIconVisible);
        Assert.False(shell.State.IsSettingsWindowCreated);
        Assert.False(shell.State.IsSettingsWindowVisible);
        Assert.Equal(1, host.PerformanceBarShowCount);
        Assert.False(host.LastPerformanceBarShowActivated);
        Assert.Equal(1, host.TrayIconCreateCount);
        Assert.Equal(0, host.SettingsWindowShowCount);
    }

    [Fact(DisplayName = "Debug 普通启动直接运行且不尝试提权")]
    public void Debug_start_does_not_request_elevation()
    {
        var shell = new StartupShellController(new RecordingStartupShellHost());

        var decision = shell.Start(new StartupAccessContext(
            IsReleaseBuild: false,
            IsElevated: false,
            ElevationAlreadyAttempted: false,
            HasExistingInstance: false,
            IsElevationHandoff: false));

        Assert.Equal(StartupAccessDecision.StartCurrentInstance, decision);
    }

    [Fact(DisplayName = "Release 普通启动最多请求一次提权")]
    public void Release_start_requests_elevation_only_before_the_attempt_marker_is_set()
    {
        var shell = new StartupShellController(new RecordingStartupShellHost());
        var firstLaunch = shell.Start(new StartupAccessContext(
            IsReleaseBuild: true,
            IsElevated: false,
            ElevationAlreadyAttempted: false,
            HasExistingInstance: false,
            IsElevationHandoff: false));
        var retriedLaunch = shell.Start(new StartupAccessContext(
            IsReleaseBuild: true,
            IsElevated: false,
            ElevationAlreadyAttempted: true,
            HasExistingInstance: false,
            IsElevationHandoff: false));

        Assert.Equal(StartupAccessDecision.StartCurrentInstanceAndAttemptElevation, firstLaunch);
        Assert.Equal(StartupAccessDecision.StartCurrentInstance, retriedLaunch);
    }

    [Theory(DisplayName = "真实 elevated 令牌直接运行，不再次请求提权")]
    [InlineData(false)]
    [InlineData(true)]
    public void Elevated_release_launch_never_requests_elevation(bool alreadyAttempted)
    {
        var shell = new StartupShellController(new RecordingStartupShellHost());

        var decision = shell.Start(new StartupAccessContext(
            IsReleaseBuild: true,
            IsElevated: true,
            ElevationAlreadyAttempted: alreadyAttempted,
            HasExistingInstance: false,
            IsElevationHandoff: false));

        Assert.Equal(StartupAccessDecision.StartCurrentInstance, decision);
    }

    [Theory(DisplayName = "普通或管理员重复启动都通知现有实例")]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_release_launch_notifies_existing_instance(bool isElevated)
    {
        var shell = new StartupShellController(new RecordingStartupShellHost());

        var decision = shell.Start(new StartupAccessContext(
            IsReleaseBuild: true,
            IsElevated: isElevated,
            ElevationAlreadyAttempted: false,
            HasExistingInstance: true,
            IsElevationHandoff: false));

        Assert.Equal(StartupAccessDecision.NotifyExistingInstanceAndExit, decision);
    }

    [Fact(DisplayName = "已提权的 runas 子进程只在有效交接时接管")]
    public void Elevated_handoff_takes_over_the_existing_instance()
    {
        var shell = new StartupShellController(new RecordingStartupShellHost());

        var decision = shell.Start(new StartupAccessContext(
            IsReleaseBuild: true,
            IsElevated: true,
            ElevationAlreadyAttempted: true,
            HasExistingInstance: true,
            IsElevationHandoff: true));

        Assert.Equal(StartupAccessDecision.HandOffToElevatedInstance, decision);
    }

    [Fact(DisplayName = "未提权的交接子进程退出并保留普通实例")]
    public void Unprivileged_handoff_does_not_replace_the_existing_instance()
    {
        var shell = new StartupShellController(new RecordingStartupShellHost());

        var decision = shell.Start(new StartupAccessContext(
            IsReleaseBuild: true,
            IsElevated: false,
            ElevationAlreadyAttempted: true,
            HasExistingInstance: true,
            IsElevationHandoff: true));

        Assert.Equal(StartupAccessDecision.RejectElevationHandoff, decision);
    }

    [Fact(DisplayName = "重复启动时即使性能条已隐藏也会手动显示并激活")]
    public void Repeated_launch_shows_and_activates_a_hidden_performance_bar()
    {
        var host = new RecordingStartupShellHost();
        var shell = new StartupShellController(host);
        shell.Start();
        host.ClickTrayLeft();

        shell.OnRepeatedLaunchRequested();

        Assert.True(shell.State.IsPerformanceBarVisible);
        Assert.Equal((true, true), host.VisibilityChanges[^1]);
    }

    [Fact(DisplayName = "托盘左键单击可切换性能条显隐")]
    public void Tray_left_click_toggles_performance_bar_visibility()
    {
        var host = new RecordingStartupShellHost();
        var shell = new StartupShellController(host);
        shell.Start();

        host.ClickTrayLeft();

        Assert.False(shell.State.IsPerformanceBarVisible);
        Assert.Equal((false, false), host.VisibilityChanges[^1]);

        host.ClickTrayLeft();

        Assert.True(shell.State.IsPerformanceBarVisible);
        Assert.Equal((true, true), host.VisibilityChanges[^1]);
    }

    [Fact(DisplayName = "托盘右键显示打开设置和退出菜单")]
    public void Tray_right_click_shows_the_basic_menu()
    {
        var host = new RecordingStartupShellHost();
        var shell = new StartupShellController(host);
        shell.Start();

        host.ClickTrayRight();
        shell.OnPerformanceBarRightClick();

        Assert.Equal(2, host.ContextMenus.Count);
        Assert.Equal(ShellMenuOrigin.Tray, host.ContextMenus[0].Origin);
        Assert.Equal(ShellMenuOrigin.PerformanceBar, host.ContextMenus[1].Origin);
        Assert.Equal(host.ContextMenus[0].Items, host.ContextMenus[1].Items);
        Assert.Equal(
        [
            new ShellMenuItem(ShellMenuAction.OpenSettings, "打开设置"),
            new ShellMenuItem(ShellMenuAction.Exit, "退出")
        ], host.ContextMenus[0].Items);
    }

    [Fact(DisplayName = "性能条左键请求原生整窗移动，右键只打开菜单")]
    public void Performance_bar_move_and_menu_actions_keep_their_mouse_semantics()
    {
        var host = new RecordingStartupShellHost();
        var shell = new StartupShellController(host);
        shell.Start();

        host.RequestPerformanceBarNativeMove();
        Assert.Equal(1, host.NativeMoveStartCount);

        shell.OnPerformanceBarRightClick();

        Assert.Equal(1, host.NativeMoveStartCount);
        Assert.Equal(ShellMenuOrigin.PerformanceBar, host.ContextMenus[^1].Origin);
    }

    [Fact(DisplayName = "从右键菜单打开设置会按需显示基础设置窗")]
    public void Selecting_open_settings_creates_and_shows_the_settings_window()
    {
        var host = new RecordingStartupShellHost();
        var shell = new StartupShellController(host);
        shell.Start();
        host.ClickTrayRight();

        host.SelectMenuItem(ShellMenuAction.OpenSettings);

        Assert.True(shell.State.IsSettingsWindowCreated);
        Assert.True(shell.State.IsSettingsWindowVisible);
        Assert.Equal(1, host.SettingsWindowShowCount);
        Assert.Equal(0, host.ShutdownCount);
    }

    [Fact(DisplayName = "关闭设置窗只隐藏设置，不退出应用")]
    public void Closing_settings_hides_it_while_the_application_keeps_running()
    {
        var host = new RecordingStartupShellHost();
        var shell = new StartupShellController(host);
        shell.Start();
        host.ClickTrayRight();
        host.SelectMenuItem(ShellMenuAction.OpenSettings);

        shell.OnSettingsWindowClosed();

        Assert.True(shell.State.IsRunning);
        Assert.True(shell.State.IsSettingsWindowCreated);
        Assert.False(shell.State.IsSettingsWindowVisible);
        Assert.Equal(1, host.SettingsWindowHideCount);
        Assert.Equal(0, host.ShutdownCount);
    }

    [Fact(DisplayName = "从基础菜单退出会清理整个应用壳")]
    public void Selecting_exit_shuts_down_the_application_shell_once()
    {
        var host = new RecordingStartupShellHost();
        var shell = new StartupShellController(host);
        shell.Start();
        host.ClickTrayRight();

        host.SelectMenuItem(ShellMenuAction.Exit);

        Assert.False(shell.State.IsRunning);
        Assert.False(shell.State.IsPerformanceBarVisible);
        Assert.False(shell.State.IsTrayIconVisible);
        Assert.False(shell.State.IsSettingsWindowVisible);
        Assert.Equal(1, host.ShutdownCount);

        host.ClickTrayLeft();
        host.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(1, host.ShutdownCount);
    }

    private sealed class RecordingStartupShellHost : IStartupShellHost
    {
        private Action? _trayLeftClick;
        private Action? _trayRightClick;
        private Action<ShellMenuAction>? _selectMenuItem;
        private Action? _performanceBarMoveRequestHandler;

        public ISystemMetricsSource? SystemMetricsSource => null;

        public List<(bool Visible, bool Activate)> VisibilityChanges { get; } = [];

        public List<(ShellMenuOrigin Origin, IReadOnlyList<ShellMenuItem> Items)> ContextMenus { get; } = [];

        public int PerformanceBarShowCount { get; private set; }

        public bool LastPerformanceBarShowActivated { get; private set; }

        public int TrayIconCreateCount { get; private set; }

        public int SettingsWindowShowCount { get; private set; }

        public int SettingsWindowHideCount { get; private set; }

        public int ShutdownCount { get; private set; }

        public int NativeMoveStartCount { get; private set; }

        public void SetPerformanceBarMoveRequestHandler(Action handler)
        {
            _performanceBarMoveRequestHandler = handler;
        }

        public void BeginPerformanceBarNativeMove()
        {
            NativeMoveStartCount++;
        }

        public void RequestPerformanceBarNativeMove() => _performanceBarMoveRequestHandler?.Invoke();

        public void ShowPerformanceBar(bool activate)
        {
            PerformanceBarShowCount++;
            LastPerformanceBarShowActivated = activate;
        }

        public void SetPerformanceBarVisible(bool visible, bool activate)
        {
            VisibilityChanges.Add((visible, activate));
        }

        public void CreateTrayIcon(Action leftClick, Action rightClick)
        {
            TrayIconCreateCount++;
            _trayLeftClick = leftClick;
            _trayRightClick = rightClick;
        }

        public void ClickTrayLeft() => _trayLeftClick?.Invoke();

        public void ClickTrayRight() => _trayRightClick?.Invoke();

        public void SelectMenuItem(ShellMenuAction action) => _selectMenuItem?.Invoke(action);

        public void ShowContextMenu(
            ShellMenuOrigin origin,
            IReadOnlyList<ShellMenuItem> items,
            Action<ShellMenuAction> selectItem)
        {
            ContextMenus.Add((origin, items.ToArray()));
            _selectMenuItem = selectItem;
        }

        public void ShowSettingsWindow()
        {
            SettingsWindowShowCount++;
        }

        public void HideSettingsWindow()
        {
            SettingsWindowHideCount++;
        }

        public void SetMetricGeneration(long generation)
        {
        }

        public void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot)
        {
        }

        public void Shutdown()
        {
            ShutdownCount++;
        }
    }
}
