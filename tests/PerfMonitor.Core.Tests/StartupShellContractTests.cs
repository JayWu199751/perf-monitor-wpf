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

    [Fact(DisplayName = "网络段优先取物理网卡，并让上下行来自总速率最高的同一接口")]
    public async Task Network_metrics_prefer_physical_adapters_and_keep_both_directions_on_one_adapter()
    {
        const ulong mebibyte = 1024UL * 1024;
        var source = new ScriptedSystemMetricsSource(
        [
            new NetworkCountersSnapshot(
                TimeSpan.FromSeconds(10),
                [
                    new NetworkInterfaceCounters(1, true, false, 14, 0, 0),
                    new NetworkInterfaceCounters(2, true, false, 14, 0, 0),
                    new NetworkInterfaceCounters(3, true, false, 0, 0, 0),
                    new NetworkInterfaceCounters(4, true, true, 14, 0, 0),
                    new NetworkInterfaceCounters(5, false, false, 14, 0, 0)
                ]),
            new NetworkCountersSnapshot(
                TimeSpan.FromSeconds(12),
                [
                    new NetworkInterfaceCounters(1, true, false, 14, 2 * mebibyte, mebibyte),
                    new NetworkInterfaceCounters(2, true, false, 14, 0, 2 * mebibyte),
                    new NetworkInterfaceCounters(3, true, false, 0, 8 * mebibyte, 8 * mebibyte),
                    new NetworkInterfaceCounters(4, true, true, 14, 100 * mebibyte, 100 * mebibyte),
                    new NetworkInterfaceCounters(5, false, false, 14, 100 * mebibyte, 100 * mebibyte),
                    new NetworkInterfaceCounters(6, true, false, 14, 500 * mebibyte, 500 * mebibyte)
                ]),
            new NetworkCountersSnapshot(
                TimeSpan.FromSeconds(13),
                [
                    new NetworkInterfaceCounters(1, true, false, 0, 2 * mebibyte, mebibyte),
                    new NetworkInterfaceCounters(2, true, false, 0, 0, 2 * mebibyte),
                    new NetworkInterfaceCounters(3, true, false, 0, 9 * mebibyte, 8 * mebibyte),
                    new NetworkInterfaceCounters(4, true, true, 14, 200 * mebibyte, 200 * mebibyte),
                    new NetworkInterfaceCounters(5, false, false, 14, 200 * mebibyte, 200 * mebibyte),
                    new NetworkInterfaceCounters(6, true, false, 0, 500 * mebibyte, 500 * mebibyte)
                ])
        ]);
        var host = new RecordingStartupShellHost(source);
        var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            var snapshots = await host.WaitForMetricSnapshotsAsync(3);

            Assert.Equal(0d, snapshots[0].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(0d, snapshots[0].NetworkUploadMegabytesPerSecond);
            Assert.Equal(1d, snapshots[1].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(0.5d, snapshots[1].NetworkUploadMegabytesPerSecond);
            Assert.Equal(1d, snapshots[2].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(0d, snapshots[2].NetworkUploadMegabytesPerSecond);
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
    }

    [Fact(DisplayName = "网络计数回退只清零该方向，非正采样间隔双向返回零且小数中点向上")]
    public async Task Network_counter_reset_is_directional_and_speed_rounding_matches_math_round()
    {
        const ulong mebibyte = 1024UL * 1024;
        var source = new ScriptedSystemMetricsSource(
        [
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(10),
                [new NetworkInterfaceCounters(7, true, false, 14, 2 * mebibyte, 3 * mebibyte)]),
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(12),
                [new NetworkInterfaceCounters(7, true, false, 14, mebibyte, 3 * mebibyte + mebibyte / 2)]),
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(12),
                [new NetworkInterfaceCounters(7, true, false, 14, 2 * mebibyte, 5 * mebibyte)])
        ]);
        var host = new RecordingStartupShellHost(source);
        var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            var snapshots = await host.WaitForMetricSnapshotsAsync(3);

            Assert.Equal(0d, snapshots[1].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(0.3d, snapshots[1].NetworkUploadMegabytesPerSecond);
            Assert.Equal(0d, snapshots[2].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(0d, snapshots[2].NetworkUploadMegabytesPerSecond);
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
    }

    [Fact(DisplayName = "网络读取失败显示缺失，恢复后仍按单调时间差与保留基线计算")]
    public async Task Network_read_failure_is_missing_and_recovery_uses_elapsed_monotonic_time()
    {
        const ulong mebibyte = 1024UL * 1024;
        var source = new ScriptedSystemMetricsSource(
        [
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(10),
                [new NetworkInterfaceCounters(9, true, false, 14, 0, 0)]),
            null,
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(12),
                [new NetworkInterfaceCounters(9, true, false, 14, 4 * mebibyte, 2 * mebibyte)])
        ]);
        var host = new RecordingStartupShellHost(source);
        var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            var snapshots = await host.WaitForMetricSnapshotsAsync(3);

            Assert.Equal(0d, snapshots[0].NetworkDownloadMegabytesPerSecond);
            Assert.Null(snapshots[1].NetworkDownloadMegabytesPerSecond);
            Assert.Null(snapshots[1].NetworkUploadMegabytesPerSecond);
            Assert.Equal(2d, snapshots[2].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(1d, snapshots[2].NetworkUploadMegabytesPerSecond);
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
    }

    private sealed class RecordingStartupShellHost : IStartupShellHost
    {
        private Action? _trayLeftClick;
        private Action? _trayRightClick;
        private Action<ShellMenuAction>? _selectMenuItem;
        private readonly object _snapshotLock = new();
        private readonly List<PerformanceMetricsSnapshot> _metricSnapshots = [];
        private readonly SemaphoreSlim _snapshotChanged = new(0);

        public RecordingStartupShellHost(ISystemMetricsSource? systemMetricsSource = null)
        {
            SystemMetricsSource = systemMetricsSource;
        }

        public ISystemMetricsSource? SystemMetricsSource { get; }

        public List<(bool Visible, bool Activate)> VisibilityChanges { get; } = [];

        public List<(ShellMenuOrigin Origin, IReadOnlyList<ShellMenuItem> Items)> ContextMenus { get; } = [];

        public int PerformanceBarShowCount { get; private set; }

        public bool LastPerformanceBarShowActivated { get; private set; }

        public int TrayIconCreateCount { get; private set; }

        public int SettingsWindowShowCount { get; private set; }

        public int SettingsWindowHideCount { get; private set; }

        public int ShutdownCount { get; private set; }

        public async Task<PerformanceMetricsSnapshot[]> WaitForMetricSnapshotsAsync(int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            while (true)
            {
                lock (_snapshotLock)
                {
                    if (_metricSnapshots.Count >= count)
                    {
                        return _metricSnapshots.Take(count).ToArray();
                    }
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"未在时限内收到 {count} 个指标快照。");
                }

                await _snapshotChanged.WaitAsync(remaining);
            }
        }

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
            lock (_snapshotLock)
            {
                _metricSnapshots.Add(snapshot);
            }

            _snapshotChanged.Release();
        }

        public void Shutdown()
        {
            ShutdownCount++;
        }
    }

    private sealed class ScriptedSystemMetricsSource(IEnumerable<NetworkCountersSnapshot?> networkSnapshots)
        : ISystemMetricsSource
    {
        private readonly NetworkCountersSnapshot?[] _snapshots = networkSnapshots.ToArray();
        private int _networkReadCount;

        public CpuTimeCounters? ReadCpuTimes() => null;

        public PhysicalMemoryCounters? ReadPhysicalMemory() => null;

        public NetworkCountersSnapshot? ReadNetworkCounters()
        {
            var index = Interlocked.Increment(ref _networkReadCount) - 1;
            return _snapshots[Math.Min(index, _snapshots.Length - 1)];
        }
    }
}
