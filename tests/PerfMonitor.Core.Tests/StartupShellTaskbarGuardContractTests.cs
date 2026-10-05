using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

/// <summary>
/// 回归测试（任务栏守卫在显隐往返后失效 bug）：性能条隐藏时守卫定时器停止；
/// 重新显示后必须重新结算行内归属并重启守卫，否则下一次任务栏遮挡将无人修复，
/// 卡片被任务栏永久覆盖（用户症状：托盘"隐藏再显示"后，点击任务栏小窗再次消失且不复原）。
/// </summary>
public sealed class StartupShellTaskbarGuardContractTests
{
    private static PerformanceSettings TaskbarRowSettings() =>
        PerformanceSettings.Default with
        {
            CenterInTaskbarRow = true,
            Widget = new WidgetPlacement { X = 900, Y = 5, Docked = DockedEdges.Top }
        };

    [Fact(DisplayName = "显隐往返后任务栏守卫恢复运转")]
    public async Task Guard_resumes_after_a_hide_and_show_cycle()
    {
        var placement = new FakePlacementPort();
        var displays = new FakeDisplaySource();
        var guard = new RecordingGuard();
        using var shell = new StartupShellController(
            new GuardRecordingHost(guard, placement, displays, new RecordingSettingsStore(TaskbarRowSettings())),
            settingsIdleRecycleTimer: new NoopRecycleTimer());

        shell.Start();

        // 模拟用户把卡片拖入任务栏行（置行内框架后触发显示器变化重新结算），
        // 守卫应以快节奏执行遮挡检查。
        placement.SimulateDragTo(900, 7);
        displays.RaiseDisplaysChanged();
        Assert.True(
            await WaitUntilAsync(() => guard.EnsureAboveCount > 0, TimeSpan.FromSeconds(2)),
            "卡片进入任务栏行后守卫应开始执行遮挡检查。");
        Assert.Equal(0, guard.GuardStoppedCount);

        // 隐藏：守卫停止并收到放回普通 z 序层的通知。
        shell.SelectMenuItem(ShellMenuAction.TogglePerformanceBarVisibility);
        Assert.False(shell.State.IsPerformanceBarVisible);
        Assert.True(
            await WaitUntilAsync(() => guard.GuardStoppedCount > 0, TimeSpan.FromSeconds(2)),
            "隐藏后应通知守卫停止。");
        var countAtHidden = guard.EnsureAboveCount;
        await Task.Delay(150);
        Assert.True(
            guard.EnsureAboveCount <= countAtHidden + 1,
            "隐藏期间守卫不应继续执行遮挡检查。");

        // 重新显示：守卫必须恢复运转（修复前定时器不会重启，此断言失败）。
        shell.SelectMenuItem(ShellMenuAction.TogglePerformanceBarVisibility);
        Assert.True(shell.State.IsPerformanceBarVisible);
        Assert.True(
            await WaitUntilAsync(
                () => guard.EnsureAboveCount > countAtHidden + 1,
                TimeSpan.FromSeconds(2)),
            "重新显示后守卫应恢复遮挡检查。");
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    private sealed class NoopRecycleTimer : ISettingsIdleRecycleTimer
    {
        public void Schedule(TimeSpan idleDelay)
        {
        }

        public void Cancel()
        {
        }

        public event EventHandler? Elapsed
        {
            add { }
            remove { }
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSettingsStore(PerformanceSettings initial) : ISettingsStore
    {
        public PerformanceSettings Load() => initial;

        public void Save(PerformanceSettings settings)
        {
        }
    }

    private sealed class RecordingGuard : ITaskbarVisibilityGuardPort
    {
        public int EnsureAboveCount;

        public int RefreshHandlesCount;

        public int GuardStoppedCount;

        public void EnsureAboveTaskbar() => Interlocked.Increment(ref EnsureAboveCount);

        public void RefreshTaskbarHandles() => Interlocked.Increment(ref RefreshHandlesCount);

        public void OnGuardStopped() => Interlocked.Increment(ref GuardStoppedCount);
    }

    private sealed class FakePlacementPort : IPerformanceBarPlacementPort
    {
        public PlacementRect Frame { get; private set; } = new(900, 5, 500, 30);

        public void SimulateDragTo(double x, double y) => Frame = Frame with { X = x, Y = y };

        public PlacementRect ReadFrame() => Frame;

        public void SetFramePosition(double x, double y) => Frame = Frame with { X = x, Y = y };
    }

    private sealed class FakeDisplaySource : IDisplayEnvironmentSource
    {
        private static readonly DisplayInformation Display = new(
            new PlacementRect(0, 0, 2560, 1440),
            new PlacementRect(0, 48, 2560, 1392),
            1.0);

        private EventHandler? _displaysChanged;

        public event EventHandler? DisplaysChanged
        {
            add => _displaysChanged += value;
            remove => _displaysChanged -= value;
        }

        public void RaiseDisplaysChanged() => _displaysChanged?.Invoke(this, EventArgs.Empty);

        public IReadOnlyList<DisplayInformation> GetDisplays() => [Display];
    }

    private sealed class GuardRecordingHost : IStartupShellHost
    {
        private readonly RecordingGuard _guard;
        private readonly FakePlacementPort _placement;
        private readonly FakeDisplaySource _displays;
        private readonly ISettingsStore _settingsStore;
        private Action? _trayLeftClick;
        private Action? _trayRightClick;
        private Action<ShellMenuAction>? _selectMenuItem;

        public GuardRecordingHost(
            RecordingGuard guard,
            FakePlacementPort placement,
            FakeDisplaySource displays,
            ISettingsStore settingsStore)
        {
            _guard = guard;
            _placement = placement;
            _displays = displays;
            _settingsStore = settingsStore;
        }

        public ISystemMetricsSource? SystemMetricsSource => null;

        ISettingsStore? IStartupShellHost.SettingsStore => _settingsStore;

        ITaskbarVisibilityGuardPort? IStartupShellHost.TaskbarVisibilityGuard => _guard;

        IPerformanceBarPlacementPort? IStartupShellHost.PerformanceBarPlacement => _placement;

        IDisplayEnvironmentSource? IStartupShellHost.DisplayEnvironmentSource => _displays;

        public void SetPerformanceBarMoveRequestHandler(Action handler)
        {
        }

        public void BeginPerformanceBarNativeMove()
        {
        }

        public void ShowPerformanceBar(bool activate)
        {
        }

        public void SetPerformanceBarVisible(bool visible, bool activate)
        {
        }

        public void CreateTrayIcon(Action leftClick, Action rightClick)
        {
            _trayLeftClick = leftClick;
            _trayRightClick = rightClick;
        }

        public void ShowContextMenu(
            ShellMenuOrigin origin,
            IReadOnlyList<ShellMenuItem> items,
            Action<ShellMenuAction> selectItem) => _selectMenuItem = selectItem;

        public void ShowSettingsWindow()
        {
        }

        public void ShowSettingsWindow(
            PerformanceSettings settings,
            Func<SettingsPatch, PerformanceSettings> updateSettings,
            bool recoveredInvalidSettings) => ShowSettingsWindow();

        public void HideSettingsWindow()
        {
        }

        public void ReleaseSettingsWindow()
        {
        }

        public void SetMetricGeneration(long generation)
        {
        }

        public void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot)
        {
        }

        public void ApplySettings(PerformanceSettings settings)
        {
        }

        public void Shutdown()
        {
        }
    }
}
