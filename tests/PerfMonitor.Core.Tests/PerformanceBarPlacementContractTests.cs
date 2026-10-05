using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

public sealed class PerformanceBarPlacementContractTests
{
    private static readonly DisplayInformation PrimaryDisplay = new(
        new PlacementRect(0, 0, 1920, 1080),
        new PlacementRect(0, 0, 1920, 1040),
        DpiScale: 1.0);

    private static readonly DisplayInformation LeftDisplay = new(
        new PlacementRect(-1920, 0, 1920, 1080),
        new PlacementRect(-1920, 0, 1920, 1040),
        DpiScale: 1.25);

    [Fact(DisplayName = "启动后按待恢复位置与贴边掩码恢复窗口位置")]
    public async Task Startup_restores_the_stored_position_and_docked_mask()
    {
        var stored = PerformanceSettings.Default with
        {
            Widget = new WidgetPlacement { X = 100, Y = 50, Docked = DockedEdges.Top }
        };
        var store = new RecordingPlacementStore(stored);
        var host = new RecordingPlacementHost(store);
        using var shell = new StartupShellController(host, placementDebounceMilliseconds: 80);

        shell.Start();

        Assert.Equal((100d, 1d), host.PlacementPort.LastSetPosition);
    }

    [Fact(DisplayName = "创建期临时位置不会覆盖待恢复位置")]
    public async Task Creation_time_temporary_position_never_overrides_the_restored_position()
    {
        var stored = PerformanceSettings.Default with
        {
            Widget = new WidgetPlacement { X = 100, Y = 1, Docked = DockedEdges.Top }
        };
        var store = new RecordingPlacementStore(stored);
        var host = new RecordingPlacementHost(store) { TemporaryFrame = new PlacementRect(500, 500, 200, 40) };
        using var shell = new StartupShellController(host, placementDebounceMilliseconds: 80);

        shell.Start();
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal((100d, 1d), host.PlacementPort.LastSetPosition);
        Assert.Null(store.LastSaved);
    }

    [Fact(DisplayName = "原生拖动结束结算贴边并按防抖持久化最后位置")]
    public async Task Native_move_settles_snapping_and_persists_with_debounce()
    {
        var store = new RecordingPlacementStore(PerformanceSettings.Default);
        var host = new RecordingPlacementHost(store)
        {
            TemporaryFrame = new PlacementRect(500, 500, 200, 40),
            FrameAfterMove = new PlacementRect(5, 300, 200, 40)
        };
        using var shell = new StartupShellController(host, placementDebounceMilliseconds: 80);
        shell.Start();

        host.RequestPerformanceBarNativeMove();

        Assert.Equal((1d, 300d), host.PlacementPort.LastSetPosition);
        await WaitAsync(() => store.LastSaved is not null);
        Assert.Equal(new WidgetPlacement { X = 1, Y = 300, Docked = DockedEdges.Left }, store.LastSaved!.Widget);
    }

    [Fact(DisplayName = "防抖期间更改其他设置会合并最新位置且不再单独保存位置")]
    public async Task Settings_changes_during_debounce_merge_the_latest_position()
    {
        var store = new RecordingPlacementStore(PerformanceSettings.Default);
        var host = new RecordingPlacementHost(store)
        {
            TemporaryFrame = new PlacementRect(500, 500, 200, 40),
            FrameAfterMove = new PlacementRect(5, 300, 200, 40)
        };
        using var shell = new StartupShellController(host, placementDebounceMilliseconds: 80);
        shell.Start();

        host.RequestPerformanceBarNativeMove();
        var saved = shell.UpdateSettings(new SettingsPatch { Opacity = 0.83 });
        await Task.Delay(300);

        Assert.Equal(new WidgetPlacement { X = 1, Y = 300, Docked = DockedEdges.Left }, saved.Widget);
        Assert.Equal(0.85, saved.Opacity);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact(DisplayName = "退出时立即写入防抖中的最后位置")]
    public async Task Exit_flushes_the_pending_latest_position()
    {
        var store = new RecordingPlacementStore(PerformanceSettings.Default);
        var host = new RecordingPlacementHost(store)
        {
            TemporaryFrame = new PlacementRect(500, 500, 200, 40),
            FrameAfterMove = new PlacementRect(5, 300, 200, 40)
        };
        using var shell = new StartupShellController(host, placementDebounceMilliseconds: 80);
        shell.Start();

        host.RequestPerformanceBarNativeMove();
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(new WidgetPlacement { X = 1, Y = 300, Docked = DockedEdges.Left }, store.LastSaved!.Widget);
    }

    [Fact(DisplayName = "重复相同位置结算不重摆窗口也不重复保存")]
    public async Task Settling_the_same_position_neither_moves_the_window_nor_saves_again()
    {
        var store = new RecordingPlacementStore(PerformanceSettings.Default);
        var host = new RecordingPlacementHost(store)
        {
            TemporaryFrame = new PlacementRect(500, 500, 200, 40),
            FrameAfterMove = new PlacementRect(1, 300, 200, 40)
        };
        using var shell = new StartupShellController(host, placementDebounceMilliseconds: 80);
        shell.Start();

        host.RequestPerformanceBarNativeMove();
        await WaitAsync(() => store.LastSaved is not null);
        var setPositionCount = host.PlacementPort.SetPositionCount;
        var saveCount = store.SaveCount;

        host.RequestPerformanceBarNativeMove();
        await Task.Delay(300);

        Assert.Equal(setPositionCount, host.PlacementPort.SetPositionCount);
        Assert.Equal(saveCount, store.SaveCount);
    }

    [Fact(DisplayName = "工作区或分辨率变化后按新工作区重新落位")]
    public async Task Display_changes_settle_the_placement_against_the_new_work_area()
    {
        var store = new RecordingPlacementStore(PerformanceSettings.Default);
        var displays = new FakeDisplaySource([PrimaryDisplay]);
        var host = new RecordingPlacementHost(store, displays)
        {
            TemporaryFrame = new PlacementRect(500, 500, 200, 40)
        };
        using var shell = new StartupShellController(host, placementDebounceMilliseconds: 80);
        shell.Start();
        host.PlacementPort.Frame = new PlacementRect(1, 50, 200, 40);

        displays.Displays = [PrimaryDisplay with
        {
            Bounds = new PlacementRect(1920, 0, 1920, 1080),
            WorkArea = new PlacementRect(1920, 0, 1920, 1040)
        }];
        displays.RaiseChanged();

        Assert.Equal((1921d, 50d), host.PlacementPort.LastSetPosition);
        await WaitAsync(() => store.LastSaved is not null);
        Assert.Equal(DockedEdges.Left, store.LastSaved!.Widget.Docked);
    }

    [Fact(DisplayName = "目标显示器消失时夹回剩余显示器工作区并持久化")]
    public async Task Disappearing_target_display_clamps_the_card_back_into_a_visible_work_area()
    {
        var store = new RecordingPlacementStore(PerformanceSettings.Default);
        var displays = new FakeDisplaySource([PrimaryDisplay, LeftDisplay]);
        var host = new RecordingPlacementHost(store, displays)
        {
            TemporaryFrame = new PlacementRect(500, 500, 200, 40)
        };
        using var shell = new StartupShellController(host, placementDebounceMilliseconds: 80);
        shell.Start();
        host.PlacementPort.Frame = new PlacementRect(-1100, 400, 200, 40);

        displays.Displays = [PrimaryDisplay];
        displays.RaiseChanged();

        Assert.Equal((1d, 400d), host.PlacementPort.LastSetPosition);
        await WaitAsync(() => store.LastSaved is not null);
        Assert.Equal(new WidgetPlacement { X = 1, Y = 400, Docked = DockedEdges.Left }, store.LastSaved!.Widget);
    }

    [Fact(DisplayName = "控制器销毁时写入最后位置")]
    public async Task Disposal_flushes_the_pending_latest_position()
    {
        var store = new RecordingPlacementStore(PerformanceSettings.Default);
        var host = new RecordingPlacementHost(store)
        {
            TemporaryFrame = new PlacementRect(500, 500, 200, 40),
            FrameAfterMove = new PlacementRect(5, 300, 200, 40)
        };
        var shell = new StartupShellController(host, placementDebounceMilliseconds: 80);
        shell.Start();

        host.RequestPerformanceBarNativeMove();
        shell.Dispose();

        Assert.Equal(new WidgetPlacement { X = 1, Y = 300, Docked = DockedEdges.Left }, store.LastSaved!.Widget);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("未在时限内等到预期状态。");
            }

            await Task.Delay(10);
        }
    }

    private sealed class RecordingPlacementStore(PerformanceSettings initialSettings) : ISettingsStore
    {
        public PerformanceSettings? LastSaved { get; private set; }

        public int SaveCount { get; private set; }

        public PerformanceSettings Load() => initialSettings;

        public void Save(PerformanceSettings settings)
        {
            SaveCount++;
            LastSaved = settings;
        }
    }

    private sealed class FakeDisplaySource(IReadOnlyList<DisplayInformation> initial) : IDisplayEnvironmentSource
    {
        public IReadOnlyList<DisplayInformation> Displays { get; set; } = initial;

        public event EventHandler? DisplaysChanged;

        public IReadOnlyList<DisplayInformation> GetDisplays() => Displays;

        public void RaiseChanged() => DisplaysChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakePlacementPort : IPerformanceBarPlacementPort
    {
        public PlacementRect Frame { get; set; } = new(24, 24, 200, 40);

        public PlacementRect TemporaryFrame { get; set; } = new(24, 24, 200, 40);

        public PlacementRect? FrameAfterMove { get; set; }

        public int SetPositionCount { get; private set; }

        public (double X, double Y)? LastSetPosition { get; private set; }

        public PlacementRect ReadFrame() => Frame;

        public void SetFramePosition(double x, double y)
        {
            SetPositionCount++;
            LastSetPosition = (x, y);
            Frame = Frame with { X = x, Y = y };
        }
    }

    private sealed class RecordingPlacementHost : IStartupShellHost
    {
        private Action? _moveRequestHandler;

        public RecordingPlacementHost(ISettingsStore? settingsStore = null, IDisplayEnvironmentSource? displays = null)
        {
            SettingsStore = settingsStore;
            DisplayEnvironmentSource = displays ?? new FakeDisplaySource([PrimaryDisplay]);
            PlacementPort = new FakePlacementPort();
        }

        public FakePlacementPort PlacementPort { get; }

        public PlacementRect TemporaryFrame
        {
            init => PlacementPort.Frame = value;
        }

        public PlacementRect? FrameAfterMove
        {
            init => PlacementPort.FrameAfterMove = value;
        }

        public IDisplayEnvironmentSource? DisplayEnvironmentSource { get; }

        public IPerformanceBarPlacementPort PerformanceBarPlacement => PlacementPort;

        public ISettingsStore? SettingsStore { get; }

        public ISystemMetricsSource? SystemMetricsSource => null;

        public void SetPerformanceBarMoveRequestHandler(Action handler) => _moveRequestHandler = handler;

        public void BeginPerformanceBarNativeMove()
        {
            if (PlacementPort.FrameAfterMove is { } frame)
            {
                PlacementPort.Frame = frame;
            }
        }

        public void RequestPerformanceBarNativeMove() => _moveRequestHandler?.Invoke();

        public void ShowPerformanceBar(bool activate) => PlacementPort.Frame = PlacementPort.TemporaryFrame;

        public void SetPerformanceBarVisible(bool visible, bool activate)
        {
        }

        public void CreateTrayIcon(Action leftClick, Action rightClick)
        {
        }

        public void ShowContextMenu(
            ShellMenuOrigin origin,
            IReadOnlyList<ShellMenuItem> items,
            Action<ShellMenuAction> selectItem)
        {
        }

        public void ShowSettingsWindow()
        {
        }

        public void HideSettingsWindow()
        {
        }

        public void SetMetricGeneration(long generation)
        {
        }

        public void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot)
        {
        }

        public void Shutdown()
        {
        }
    }
}
