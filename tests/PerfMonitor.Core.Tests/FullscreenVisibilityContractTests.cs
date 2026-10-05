using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

/// <summary>
/// F09 显隐与全屏自动隐藏：全屏边沿驱动的性能条显隐状态机合同。
/// 通过 StartupShellController 合同与可替换的全屏观察者覆盖规格状态转移表。
/// </summary>
public sealed class FullscreenVisibilityContractTests
{
    [Fact(DisplayName = "可见且非手动隐藏时进入全屏会隐藏性能条且不抢焦点")]
    public void Entering_fullscreen_hides_the_visible_bar_without_activation()
    {
        var (shell, host, watcher) = CreateRunningShell();

        watcher.EnterFullscreen();

        Assert.False(shell.State.IsPerformanceBarVisible);
        Assert.Equal((false, false), host.VisibilityChanges[^1]);
    }

    [Fact(DisplayName = "自动隐藏后退出全屏恢复显示且不抢焦点")]
    public void Exiting_fullscreen_restores_the_auto_hidden_bar_without_activation()
    {
        var (shell, host, watcher) = CreateRunningShell();
        watcher.EnterFullscreen();

        watcher.ExitFullscreen();

        Assert.True(shell.State.IsPerformanceBarVisible);
        Assert.Equal((true, false), host.VisibilityChanges[^1]);
    }

    [Fact(DisplayName = "手动隐藏的性能条在全屏进出时保持隐藏")]
    public void Manually_hidden_bar_stays_hidden_across_fullscreen_transitions()
    {
        var (shell, host, watcher) = CreateRunningShell();
        host.ClickTrayLeft();
        var hideCountAtFullscreenEnter = host.VisibilityChanges.Count;

        watcher.EnterFullscreen();

        Assert.False(shell.State.IsPerformanceBarVisible);
        Assert.Equal(hideCountAtFullscreenEnter, host.VisibilityChanges.Count);

        watcher.ExitFullscreen();

        Assert.False(shell.State.IsPerformanceBarVisible);
        Assert.Equal(hideCountAtFullscreenEnter, host.VisibilityChanges.Count);
    }

    [Fact(DisplayName = "自动隐藏期间手动显示清两种隐藏标记且同一全屏周期内保持显示")]
    public void Manual_show_during_auto_hidden_fullscreen_keeps_the_bar_visible_for_the_session()
    {
        var (shell, host, watcher) = CreateRunningShell();
        watcher.EnterFullscreen();
        host.ClickTrayLeft();
        Assert.True(shell.State.IsPerformanceBarVisible);
        var showCountAtManualShow = host.VisibilityChanges.Count;

        // 同一全屏周期持续期间没有新的进入边沿，保持显示。
        Assert.Equal(showCountAtManualShow, host.VisibilityChanges.Count);

        // 退出全屏不再重复显示或聚焦。
        watcher.ExitFullscreen();
        Assert.True(shell.State.IsPerformanceBarVisible);
        Assert.Equal(showCountAtManualShow, host.VisibilityChanges.Count);

        // 新一轮进入全屏恢复正常自动隐藏规则。
        watcher.EnterFullscreen();
        Assert.False(shell.State.IsPerformanceBarVisible);
        Assert.Equal((false, false), host.VisibilityChanges[^1]);
    }

    [Fact(DisplayName = "自动隐藏期间重复启动唤起走手动显示入口并保持同周期显示")]
    public void Repeated_launch_acts_as_a_manual_show_during_auto_hidden_fullscreen()
    {
        var (shell, host, watcher) = CreateRunningShell();
        watcher.EnterFullscreen();
        Assert.False(shell.State.IsPerformanceBarVisible);

        shell.OnRepeatedLaunchRequested();

        Assert.True(shell.State.IsPerformanceBarVisible);
        Assert.Equal((true, true), host.VisibilityChanges[^1]);

        watcher.ExitFullscreen();

        Assert.True(shell.State.IsPerformanceBarVisible);
        // 退出全屏不再重复显示或聚焦。
        Assert.Equal(2, host.VisibilityChanges.Count);
    }

    [Fact(DisplayName = "关闭自动隐藏时仅恢复因自动原因隐藏的性能条")]
    public void Disabling_auto_hide_restores_only_the_auto_hidden_bar()
    {
        var (shell, host, watcher) = CreateRunningShell();
        watcher.EnterFullscreen();
        Assert.False(shell.State.IsPerformanceBarVisible);

        shell.UpdateSettings(new SettingsPatch { AutoHideOnFullscreen = false });

        Assert.True(shell.State.IsPerformanceBarVisible);
        Assert.Equal((true, false), host.VisibilityChanges[^1]);
    }

    [Fact(DisplayName = "关闭自动隐藏时手动隐藏状态仍保持隐藏")]
    public void Disabling_auto_hide_keeps_the_manually_hidden_bar_hidden()
    {
        var (shell, host, watcher) = CreateRunningShell();
        host.ClickTrayLeft();
        var hideCountAtDisable = host.VisibilityChanges.Count;

        shell.UpdateSettings(new SettingsPatch { AutoHideOnFullscreen = false });

        Assert.False(shell.State.IsPerformanceBarVisible);
        Assert.Equal(hideCountAtDisable, host.VisibilityChanges.Count);
    }

    [Fact(DisplayName = "关闭自动隐藏后进入全屏不再隐藏，重新开启恢复观察")]
    public void Re_enabling_auto_hide_resumes_fullscreen_hiding()
    {
        var (shell, host, watcher) = CreateRunningShell();

        shell.UpdateSettings(new SettingsPatch { AutoHideOnFullscreen = false });
        watcher.EnterFullscreen();
        Assert.True(shell.State.IsPerformanceBarVisible);

        shell.UpdateSettings(new SettingsPatch { AutoHideOnFullscreen = true });
        watcher.EnterFullscreen();
        Assert.False(shell.State.IsPerformanceBarVisible);
    }

    [Fact(DisplayName = "重复的全屏边沿通知是幂等的，持续全屏不重复覆盖用户选择")]
    public void Repeated_fullscreen_notifications_are_idempotent()
    {
        var (shell, host, watcher) = CreateRunningShell();

        watcher.EnterFullscreen();
        watcher.EnterFullscreen();
        Assert.Equal(1, host.VisibilityChanges.Count(v => v.Visible == false));

        watcher.ExitFullscreen();
        watcher.ExitFullscreen();
        Assert.Equal(1, host.VisibilityChanges.Count(v => v.Visible == true && !v.Activate));
    }

    [Fact(DisplayName = "启动时开启自动隐藏即启动全屏观察，退出时停止")]
    public void Watcher_starts_on_run_and_stops_on_exit()
    {
        var (shell, _, watcher) = CreateRunningShell();

        Assert.Equal(1, watcher.StartCount);

        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(1, watcher.StopCount);
    }

    [Fact(DisplayName = "启动时关闭自动隐藏则不启动全屏观察")]
    public void Watcher_is_not_started_when_auto_hide_is_disabled()
    {
        var store = new RecordingSettingsStore(
            PerformanceSettings.Default with { AutoHideOnFullscreen = false });
        var watcher = new FakeFullscreenWatcher();
        var host = new RecordingStartupShellHost(settingsStore: store, fullscreenWatcher: watcher);
        using var shell = new StartupShellController(host);

        shell.Start();

        Assert.Equal(0, watcher.StartCount);
    }

    [Fact(DisplayName = "控制器释放时停止全屏观察")]
    public void Disposing_the_controller_stops_the_fullscreen_watcher()
    {
        var watcher = new FakeFullscreenWatcher();
        var host = new RecordingStartupShellHost(fullscreenWatcher: watcher);
        var shell = new StartupShellController(host);
        shell.Start();

        shell.Dispose();

        Assert.Equal(1, watcher.StopCount);
    }

    [Fact(DisplayName = "自动隐藏期间暂停快慢采样，退出全屏恢复并立即补采样")]
    public async Task Auto_hidden_bar_pauses_sampling_and_exiting_fullscreen_resumes_immediately()
    {
        var source = new CountingSystemMetricsSource();
        var slowSource = new CountingSlowMetricsSource();
        var watcher = new FakeFullscreenWatcher();
        var host = new RecordingStartupShellHost(
            systemMetricsSource: source,
            slowMetricsSource: slowSource,
            fullscreenWatcher: watcher);
        using var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            await source.WaitForCpuReadCountAsync(1, TimeSpan.FromSeconds(2));
            await slowSource.WaitForTemperatureReadCountAsync(1, TimeSpan.FromSeconds(2));

            watcher.EnterFullscreen();
            await Task.Delay(3300);

            Assert.Equal(1, source.CpuReadCount);
            Assert.Equal(1, slowSource.TemperatureReadCount);

            watcher.ExitFullscreen();
            await source.WaitForCpuReadCountAsync(2, TimeSpan.FromSeconds(2));
            await slowSource.WaitForTemperatureReadCountAsync(2, TimeSpan.FromSeconds(2));
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
    }

    [Fact(DisplayName = "暂停后的首次网络采样建立新基线，下一轮才算出速率")]
    public async Task First_network_sample_after_a_pause_rebases_and_the_next_round_reports_speed()
    {
        const ulong mebibyte = 1024UL * 1024;
        var source = new ScriptedNetworkMetricsSource(
        [
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(10),
                [new NetworkInterfaceCounters(21, true, false, 14, 0, 0)]),
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(40),
                [new NetworkInterfaceCounters(21, true, false, 14, 6 * mebibyte, 0)]),
            new NetworkCountersSnapshot(TimeSpan.FromSeconds(41),
                [new NetworkInterfaceCounters(21, true, false, 14, 9 * mebibyte, 0)])
        ]);
        var watcher = new FakeFullscreenWatcher();
        var host = new RecordingStartupShellHost(systemMetricsSource: source, fullscreenWatcher: watcher);
        using var shell = new StartupShellController(host);

        shell.Start();
        try
        {
            await host.WaitForMetricSnapshotsAsync(1);
            watcher.EnterFullscreen();
            await Task.Delay(1300);
            Assert.Equal(1, source.NetworkReadCount);

            watcher.ExitFullscreen();
            var snapshots = await host.WaitForMetricSnapshotsAsync(3);

            // 暂停后的首轮只建立基线，速率必须是 0 而不是把暂停期误算成瞬时爆量。
            Assert.Equal(0d, snapshots[1].NetworkDownloadMegabytesPerSecond);
            Assert.Equal(3d, snapshots[2].NetworkDownloadMegabytesPerSecond);
        }
        finally
        {
            shell.SelectMenuItem(ShellMenuAction.Exit);
        }
    }

    private static (StartupShellController Shell, RecordingStartupShellHost Host, FakeFullscreenWatcher Watcher)
        CreateRunningShell()
    {
        var watcher = new FakeFullscreenWatcher();
        var host = new RecordingStartupShellHost(fullscreenWatcher: watcher);
        var shell = new StartupShellController(host);
        shell.Start();
        return (shell, host, watcher);
    }

    private sealed class FakeFullscreenWatcher : IFullscreenWatcher
    {
        private Action<bool>? _onFullscreenChanged;

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public void Start(Action<bool> onFullscreenChanged)
        {
            StartCount++;
            _onFullscreenChanged = onFullscreenChanged;
        }

        public void Stop()
        {
            StopCount++;
            _onFullscreenChanged = null;
        }

        public void EnterFullscreen() => _onFullscreenChanged?.Invoke(true);

        public void ExitFullscreen() => _onFullscreenChanged?.Invoke(false);

        public void Dispose() => Stop();
    }

    private sealed class RecordingSettingsStore(PerformanceSettings initialSettings) : ISettingsStore
    {
        public PerformanceSettings? LastSaved { get; private set; }

        public PerformanceSettings Load() => initialSettings;

        public void Save(PerformanceSettings settings) => LastSaved = settings;
    }

    private sealed class RecordingStartupShellHost(
        IFullscreenWatcher? fullscreenWatcher = null,
        ISettingsStore? settingsStore = null,
        ISystemMetricsSource? systemMetricsSource = null,
        ISlowMetricsSource? slowMetricsSource = null) : IStartupShellHost
    {
        private Action? _trayLeftClick;
        private Action? _trayRightClick;
        private Action<ShellMenuAction>? _selectMenuItem;
        private Func<SettingsPatch, PerformanceSettings>? _updateSettings;
        private readonly object _snapshotLock = new();
        private readonly List<PerformanceMetricsSnapshot> _metricSnapshots = [];
        private readonly SemaphoreSlim _snapshotChanged = new(0);

        public ISystemMetricsSource? SystemMetricsSource { get; } = systemMetricsSource;

        public ISlowMetricsSource? SlowMetricsSource { get; } = slowMetricsSource;

        public ISettingsStore? SettingsStore { get; } = settingsStore;

        public IFullscreenWatcher? FullscreenWatcher { get; } = fullscreenWatcher;

        public List<(bool Visible, bool Activate)> VisibilityChanges { get; } = [];

        public List<PerformanceSettings> AppliedSettings { get; } = [];

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

        public void SetPerformanceBarMoveRequestHandler(Action handler)
        {
        }

        public void BeginPerformanceBarNativeMove()
        {
        }

        public void ShowPerformanceBar(bool activate)
        {
        }

        public void SetPerformanceBarVisible(bool visible, bool activate) => VisibilityChanges.Add((visible, activate));

        public void CreateTrayIcon(Action leftClick, Action rightClick)
        {
            _trayLeftClick = leftClick;
            _trayRightClick = rightClick;
        }

        public void ClickTrayLeft() => _trayLeftClick?.Invoke();

        public void ClickTrayRight() => _trayRightClick?.Invoke();

        public void ShowContextMenu(
            ShellMenuOrigin origin,
            IReadOnlyList<ShellMenuItem> items,
            Action<ShellMenuAction> selectItem) => _selectMenuItem = selectItem;

        public void SelectMenuItem(ShellMenuAction action) => _selectMenuItem?.Invoke(action);

        public void ShowSettingsWindow(PerformanceSettings settings, Func<SettingsPatch, PerformanceSettings> updateSettings, bool recoveredInvalidSettings)
        {
            _updateSettings = updateSettings;
        }

        public PerformanceSettings UpdateSettings(SettingsPatch patch) =>
            _updateSettings?.Invoke(patch) ?? throw new InvalidOperationException("设置窗未创建");

        public void ShowSettingsWindow()
        {
        }

        public void HideSettingsWindow()
        {
        }

        public void ApplySettings(PerformanceSettings settings) => AppliedSettings.Add(settings);

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
        }
    }

    private sealed class ScriptedNetworkMetricsSource(IEnumerable<NetworkCountersSnapshot?> networkSnapshots)
        : ISystemMetricsSource
    {
        private readonly NetworkCountersSnapshot?[] _snapshots = networkSnapshots.ToArray();
        private int _networkReadCount;

        public int NetworkReadCount => Volatile.Read(ref _networkReadCount);

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
}
