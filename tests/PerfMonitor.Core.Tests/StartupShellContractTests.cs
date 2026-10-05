using PerfMonitor.Core.Shell;

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

        public void Shutdown()
        {
            ShutdownCount++;
        }
    }
}
