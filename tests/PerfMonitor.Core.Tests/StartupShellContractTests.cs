using PerfMonitor.Core.Shell;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;

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

    [Fact(DisplayName = "指标设置从单一持久化真值加载并在表单更改时深合并保存")]
    public void Settings_load_from_store_and_form_updates_are_validated_deep_merged_and_applied()
    {
        var initial = PerformanceSettings.Default with
        {
            Metrics = PerformanceSettings.Default.Metrics with { Memory = false },
            FontSize = 15
        };
        var store = new RecordingSettingsStore(initial);
        var host = new RecordingStartupShellHost(settingsStore: store);
        var shell = new StartupShellController(host);
        shell.Start();
        host.ClickTrayRight();
        host.SelectMenuItem(ShellMenuAction.OpenSettings);

        var saved = host.UpdateSettings(new SettingsPatch
        {
            Metrics = new MetricsSettingsPatch { Cpu = false, Network = false },
            FastRefreshMilliseconds = 2000,
            Opacity = 0.83
        });

        Assert.False(saved.Metrics.Cpu);
        Assert.False(saved.Metrics.Network);
        Assert.False(saved.Metrics.Memory);
        Assert.True(saved.Metrics.Gpu);
        Assert.Equal(2000, saved.FastRefreshMilliseconds);
        Assert.Equal(15, saved.FontSize);
        Assert.Equal(0.85, saved.Opacity);
        Assert.Equal(saved, shell.Settings);
        Assert.Equal(saved, store.LastSaved);
        Assert.Equal(saved, host.AppliedSettings[^1]);
    }

    [Fact(DisplayName = "持久化的刷新间隔控制启动后的下一次采样时刻")]
    public async Task Persisted_fast_refresh_interval_controls_the_next_sample_after_startup()
    {
        var source = new CountingSystemMetricsSource();
        var storedSettings = PerformanceSettings.Default with { FastRefreshMilliseconds = 5000 };
        var host = new RecordingStartupShellHost(source, new RecordingSettingsStore(storedSettings));
        using var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            await source.WaitForCpuReadCountAsync(1, TimeSpan.FromSeconds(2));
            await Task.Delay(1300);

            Assert.Equal(1, source.CpuReadCount);
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
    }

    [Fact(DisplayName = "运行中更改快刷新间隔会重排下一次采样")]
    public async Task Changing_fast_refresh_interval_reschedules_the_next_sample()
    {
        var source = new CountingSystemMetricsSource();
        var host = new RecordingStartupShellHost(source);
        using var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            await source.WaitForCpuReadCountAsync(1, TimeSpan.FromSeconds(2));
            var readsBeforeUpdate = source.CpuReadCount;
            shell.UpdateSettings(new SettingsPatch { FastRefreshMilliseconds = 5000 });

            await source.WaitForCpuReadCountAsync(readsBeforeUpdate + 1, TimeSpan.FromSeconds(2));
            await Task.Delay(1300);

            Assert.Equal(readsBeforeUpdate + 1, source.CpuReadCount);
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
    }

    [Fact(DisplayName = "运行中更改慢刷新间隔会重排下一次采样")]
    public async Task Changing_slow_refresh_interval_reschedules_the_next_sample()
    {
        var source = new CountingSlowMetricsSource();
        var host = new RecordingStartupShellHost(slowMetricsSource: source);
        using var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            await source.WaitForTemperatureReadCountAsync(1, TimeSpan.FromSeconds(2));
            shell.UpdateSettings(new SettingsPatch { SlowRefreshMilliseconds = 5000 });

            await source.WaitForTemperatureReadCountAsync(2, TimeSpan.FromSeconds(2));
            await Task.Delay(3300);

            Assert.Equal(2, source.TemperatureReadCount);
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
    }

    [Fact(DisplayName = "性能条隐藏或原生拖动期间更改刷新率不会恢复采样")]
    public async Task Refresh_change_does_not_resume_sampling_while_hidden_or_native_moving()
    {
        var source = new CountingSystemMetricsSource();
        var host = new RecordingStartupShellHost(source);
        using var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            await source.WaitForCpuReadCountAsync(1, TimeSpan.FromSeconds(2));
            host.ClickTrayLeft();
            shell.UpdateSettings(new SettingsPatch { FastRefreshMilliseconds = 5000 });
            await Task.Delay(1200);
            Assert.Equal(1, source.CpuReadCount);

            host.ClickTrayLeft();
            await source.WaitForCpuReadCountAsync(2, TimeSpan.FromSeconds(2));

            var readsDuringNativeMove = -1;
            host.DuringNativeMove = () =>
            {
                shell.UpdateSettings(new SettingsPatch { FastRefreshMilliseconds = 1000 });
                Thread.Sleep(1200);
                readsDuringNativeMove = source.CpuReadCount;
            };
            host.RequestPerformanceBarNativeMove();

            Assert.Equal(2, readsDuringNativeMove);
            await source.WaitForCpuReadCountAsync(3, TimeSpan.FromSeconds(2));
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
    }

    [Theory(DisplayName = "用户调整的不透明度按 0.05 步进并保留合法边界")]
    [InlineData(0.83, 0.85)]
    [InlineData(0.72, 0.70)]
    [InlineData(0.20, 0.20)]
    [InlineData(0.999, 1.00)]
    public void Opacity_patches_are_quantized_to_five_percent_steps(double input, double expected)
    {
        var changed = PerformanceSettings.Default.Apply(new SettingsPatch { Opacity = input });

        Assert.Equal(expected, changed.Opacity);
    }

    [Fact(DisplayName = "非法刷新间隔不会改变或持久化中央设置")]
    public void Invalid_settings_patch_is_rejected_without_changing_or_saving_settings()
    {
        var store = new RecordingSettingsStore(PerformanceSettings.Default);
        var host = new RecordingStartupShellHost(settingsStore: store);
        var shell = new StartupShellController(host);
        shell.Start();

        Assert.Throws<ArgumentOutOfRangeException>(() => shell.UpdateSettings(
            new SettingsPatch { FastRefreshMilliseconds = 1500 }));

        Assert.Equal(PerformanceSettings.Default, shell.Settings);
        Assert.Null(store.LastSaved);
    }

    [Fact(DisplayName = "持久化失败时中央设置和性能条都保留上一个成功值")]
    public void Save_failure_does_not_publish_or_apply_a_settings_change()
    {
        var store = new RecordingSettingsStore(PerformanceSettings.Default)
        {
            SaveFailure = new IOException("模拟文件写入失败")
        };
        var host = new RecordingStartupShellHost(settingsStore: store);
        var shell = new StartupShellController(host);
        shell.Start();

        Assert.Throws<IOException>(() => shell.UpdateSettings(new SettingsPatch
        {
            Metrics = new MetricsSettingsPatch { Cpu = false }
        }));

        Assert.Equal(PerformanceSettings.Default, shell.Settings);
        Assert.Single(host.AppliedSettings);
        Assert.Null(store.LastSaved);
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

    [Fact(DisplayName = "重复启动恢复隐藏性能条后立即恢复采样")]
    public async Task Repeated_launch_resumes_sampling_after_restoring_hidden_bar()
    {
        var source = new CountingSystemMetricsSource();
        var host = new RecordingStartupShellHost(source);
        using var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            await source.WaitForCpuReadCountAsync(1, TimeSpan.FromSeconds(2));
            host.ClickTrayLeft();
            await Task.Delay(1200);
            Assert.Equal(1, source.CpuReadCount);

            shell.OnRepeatedLaunchRequested();
            await source.WaitForCpuReadCountAsync(2, TimeSpan.FromSeconds(2));
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
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

    [Fact(DisplayName = "完整接口表中消失的网卡重现时重新建立零速率基线")]
    public async Task Reappearing_network_interface_starts_with_a_fresh_baseline()
    {
        const ulong mebibyte = 1024UL * 1024;
        var source = new ScriptedSystemMetricsSource(
        [
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(10),
                [new NetworkInterfaceCounters(11, true, false, 14, 0, 0)]),
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(11), []),
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(12),
                [new NetworkInterfaceCounters(11, true, false, 14, 4 * mebibyte, 2 * mebibyte)])
        ]);
        var host = new RecordingStartupShellHost(source);
        var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            var snapshots = await host.WaitForMetricSnapshotsAsync(3);

            Assert.Equal(0d, snapshots[0].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(0d, snapshots[1].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(0d, snapshots[2].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(0d, snapshots[2].NetworkUploadMegabytesPerSecond);
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
        private Action? _performanceBarMoveRequestHandler;

        public RecordingStartupShellHost(
            ISystemMetricsSource? systemMetricsSource = null,
            ISettingsStore? settingsStore = null,
            ISlowMetricsSource? slowMetricsSource = null)
        {
            SystemMetricsSource = systemMetricsSource;
            SettingsStore = settingsStore;
            SlowMetricsSource = slowMetricsSource;
        }

        public ISystemMetricsSource? SystemMetricsSource { get; }

        public ISlowMetricsSource? SlowMetricsSource { get; }

        public ISettingsStore? SettingsStore { get; }

        public List<PerformanceSettings> AppliedSettings { get; } = [];

        private Func<SettingsPatch, PerformanceSettings>? _updateSettings;

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

        public int NativeMoveStartCount { get; private set; }

        public Action? DuringNativeMove { get; set; }

        public void SetPerformanceBarMoveRequestHandler(Action handler)
        {
            _performanceBarMoveRequestHandler = handler;
        }

        public void BeginPerformanceBarNativeMove()
        {
            NativeMoveStartCount++;
            DuringNativeMove?.Invoke();
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

        public void ShowSettingsWindow(
            PerformanceSettings settings,
            Func<SettingsPatch, PerformanceSettings> updateSettings,
            bool recoveredInvalidSettings)
        {
            SettingsWindowShowCount++;
            _updateSettings = updateSettings;
        }

        public PerformanceSettings UpdateSettings(SettingsPatch patch) =>
            _updateSettings?.Invoke(patch) ?? throw new InvalidOperationException("设置窗未创建");

        public void ApplySettings(PerformanceSettings settings) => AppliedSettings.Add(settings);

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

    private sealed class CountingSystemMetricsSource : ISystemMetricsSource
    {
        private int _cpuReadCount;

        public int CpuReadCount => Volatile.Read(ref _cpuReadCount);

        public CpuTimeCounters? ReadCpuTimes()
        {
            Interlocked.Increment(ref _cpuReadCount);
            return null;
        }

        public PhysicalMemoryCounters? ReadPhysicalMemory() => null;

        public NetworkCountersSnapshot? ReadNetworkCounters() => null;

        public async Task WaitForCpuReadCountAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (CpuReadCount < count)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"未在时限内读取到 {count} 次 CPU 计数器。");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(10, remaining.TotalMilliseconds)));
            }
        }
    }

    private sealed class CountingSlowMetricsSource : ISlowMetricsSource
    {
        private int _temperatureReadCount;

        public int TemperatureReadCount => Volatile.Read(ref _temperatureReadCount);

        public Task<GpuMetricsReading?> ReadGpuMetricsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<GpuMetricsReading?>(null);

        public Task<int?> ReadCpuTemperatureCelsiusAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _temperatureReadCount);
            return Task.FromResult<int?>(null);
        }

        public async Task WaitForTemperatureReadCountAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (TemperatureReadCount < count)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException($"未在时限内读取到 {count} 次温度计数器。");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(10, remaining.TotalMilliseconds)));
            }
        }
    }

    private sealed class RecordingSettingsStore(PerformanceSettings initialSettings) : ISettingsStore
    {
        public PerformanceSettings? LastSaved { get; private set; }

        public Exception? SaveFailure { get; init; }

        public PerformanceSettings Load() => initialSettings;

        public void Save(PerformanceSettings settings)
        {
            if (SaveFailure is not null)
            {
                throw SaveFailure;
            }

            LastSaved = settings;
        }
    }
}
