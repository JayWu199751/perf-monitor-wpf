using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.Core.Shell;

public enum ShellMenuOrigin
{
    PerformanceBar,
    Tray
}

public enum ShellMenuAction
{
    OpenSettings,
    Exit,
    TogglePerformanceBarVisibility,
    ToggleCenterInTaskbarRow,
    ToggleTransparentDisplay,
    Separator
}

public sealed record ShellMenuItem(
    ShellMenuAction Action,
    string Label,
    bool IsCheckable = false,
    bool IsChecked = false);

public sealed record StartupShellState(
    bool IsRunning,
    bool IsPerformanceBarVisible,
    bool IsTrayIconVisible,
    bool IsSettingsWindowCreated,
    bool IsSettingsWindowVisible,
    bool IsSettingsWindowRecyclePending = false);

public sealed record StartupAccessContext(
    bool IsReleaseBuild,
    bool IsElevated,
    bool ElevationAlreadyAttempted,
    bool HasExistingInstance,
    bool IsElevationHandoff);

public enum StartupAccessDecision
{
    StartCurrentInstance,
    StartCurrentInstanceAndAttemptElevation,
    NotifyExistingInstanceAndExit,
    HandOffToElevatedInstance,
    RejectElevationHandoff
}

/// <summary>
/// 全屏前台窗口观察能力端口：平台适配层注入，控制器只在启动/停止生命周期上消费其边沿通知。
/// </summary>
public interface IFullscreenWatcher : IDisposable
{
    void Start(Action<bool> onFullscreenChanged);

    void Stop();
}

public enum AutostartRequestOutcome
{
    Enabled,
    Disabled,
    Failed
}

public interface IAutostartPort
{
    AutostartRequestOutcome TrySetEnabled(bool enabled);
}

/// <summary>
/// 设置窗闲置回收的单次定时端口；控制器负责排程与取消，到期回调通知回收时刻。
/// Core 只依赖该端口，不依赖 WPF 定时器，便于以注入方式测试闲置状态机。
/// </summary>
public interface ISettingsIdleRecycleTimer : IDisposable
{
    /// <summary>到期后触发一次 <see cref="Elapsed"/>；重复排程应替换前次待触发项。</summary>
    void Schedule(TimeSpan idleDelay);

    /// <summary>取消当前待触发项；没有待触发项时无操作。可重复调用。</summary>
    void Cancel();

    event EventHandler? Elapsed;
}

public interface IStartupShellHost
{
    void SetPerformanceBarMoveRequestHandler(Action handler);

    /// <summary>
    /// 注册性能条框架尺寸变化回调；窗口尺寸随内容渲染、字号调整等变化时宿主应调用回调。
    /// 行内居中需要按实际宽度重新回中，缺省空实现表示宿主不支持尺寸通知。
    /// </summary>
    void SetPerformanceBarFrameSizeChangedHandler(Action handler)
    {
    }

    void BeginPerformanceBarNativeMove();

    ISystemMetricsSource? SystemMetricsSource { get; }

    ISlowMetricsSource? SlowMetricsSource => null;

    /// <summary>显示器与工作区信息端口；缺省表示宿主不提供落位能力。</summary>
    IDisplayEnvironmentSource? DisplayEnvironmentSource => null;

    /// <summary>性能条窗口框架读写端口；窗口重建后宿主必须返回新实例。</summary>
    IPerformanceBarPlacementPort? PerformanceBarPlacement => null;

    /// <summary>任务栏可见性守卫端口；缺省表示宿主不提供守卫能力。</summary>
    ITaskbarVisibilityGuardPort? TaskbarVisibilityGuard => null;

    /// <summary>全屏前台窗口观察端口；缺省表示宿主不支持全屏自动隐藏。</summary>
    IFullscreenWatcher? FullscreenWatcher => null;

    void ShowPerformanceBar(bool activate);

    void SetPerformanceBarVisible(bool visible, bool activate);

    void CreateTrayIcon(Action leftClick, Action rightClick);

    void ShowContextMenu(
        ShellMenuOrigin origin,
        IReadOnlyList<ShellMenuItem> items,
        Action<ShellMenuAction> selectItem);

    void ShowSettingsWindow();

    ISettingsStore? SettingsStore => null;

    IAutostartPort? AutostartPort => null;

    void ShowSettingsWindow(
        PerformanceSettings settings,
        Func<SettingsPatch, PerformanceSettings> updateSettings,
        bool recoveredInvalidSettings) => ShowSettingsWindow();

    void ApplySettings(PerformanceSettings settings)
    {
    }

    void HideSettingsWindow();

    /// <summary>释放设置窗实例（闲置回收）；宿主负责在 UI 线程关闭窗口并丢弃引用。</summary>
    void ReleaseSettingsWindow()
    {
    }

    void SetMetricGeneration(long generation);

    void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot);

    void Shutdown();
}

public sealed class StartupShellController : IDisposable
{
    private readonly IStartupShellHost _host;
    private readonly ISettingsStore? _settingsStore;
    private readonly IAutostartPort? _autostartPort;
    private readonly PerformanceMetricsSampler? _metricsSampler;
    private readonly PerformanceSlowMetricsSampler? _slowMetricsSampler;
    private readonly IDisplayEnvironmentSource? _displayEnvironmentSource;
    private readonly PlacementPersistence? _placementPersistence;
    private readonly IFullscreenWatcher? _fullscreenWatcher;
    private readonly object _metricsSnapshotSync = new();
    private readonly object _placementSync = new();
    private PerformanceMetricsSnapshot? _latestMetricsSnapshot;
    private long _metricsGeneration;
    private bool _isPerformanceBarNativeMoveActive;
    private IPerformanceBarPlacementPort? _placementPort;
    private WidgetPlacement _lastPlacement = new();
    private bool _isSettlingPlacement;
    private readonly ITaskbarVisibilityGuardPort? _taskbarGuard;
    private readonly TimeSpan _taskbarGuardFastInterval;
    private readonly TimeSpan _taskbarGuardSlowInterval;
    private readonly TimeSpan _settingsIdleRecycleDelay;
    private readonly ISettingsIdleRecycleTimer _settingsRecycleTimer;
    private Timer? _taskbarGuardFastTimer;
    private Timer? _taskbarGuardSlowTimer;
    private bool _isTaskbarGuardActive;
    private bool _isInTaskbarRow;
    private bool _isFullscreenActive;
    private bool _isAutoHiddenByFullscreen;
    private bool _isManuallyHidden;
    private bool _isFullscreenWatcherRunning;
    private bool _disposed;

    public StartupShellController(
        IStartupShellHost host,
        int? fastRefreshMilliseconds = null,
        int? slowRefreshMilliseconds = null,
        int? placementDebounceMilliseconds = null,
        int? taskbarGuardFastMilliseconds = null,
        int? taskbarGuardSlowMilliseconds = null,
        TimeSpan? settingsIdleRecycleDelay = null,
        ISettingsIdleRecycleTimer? settingsIdleRecycleTimer = null)
    {
        _host = host;
        _settingsStore = host.SettingsStore;
        _fullscreenWatcher = host.FullscreenWatcher;
        _autostartPort = host.AutostartPort;
        _taskbarGuard = host.TaskbarVisibilityGuard;
        _taskbarGuardFastInterval = TimeSpan.FromMilliseconds(taskbarGuardFastMilliseconds ?? 30);
        _taskbarGuardSlowInterval = TimeSpan.FromMilliseconds(taskbarGuardSlowMilliseconds ?? 300);
        _settingsIdleRecycleDelay = settingsIdleRecycleDelay ?? SettingsIdleRecycleDefaults.Delay;
        _settingsRecycleTimer = settingsIdleRecycleTimer ?? new SettingsIdleRecycleTimer();
        _settingsRecycleTimer.Elapsed += OnSettingsIdleRecycleElapsed;
        var loadedSettings = (_settingsStore?.Load() ?? PerformanceSettings.Default).Validate();
        Settings = (loadedSettings with
        {
            FastRefreshMilliseconds = fastRefreshMilliseconds ?? loadedSettings.FastRefreshMilliseconds,
            SlowRefreshMilliseconds = slowRefreshMilliseconds ?? loadedSettings.SlowRefreshMilliseconds
        }).Validate();
        _host.SetPerformanceBarMoveRequestHandler(OnPerformanceBarNativeMoveRequested);
        _host.SetPerformanceBarFrameSizeChangedHandler(OnPerformanceBarFrameSizeChanged);
        _lastPlacement = Settings.Widget;
        _displayEnvironmentSource = host.DisplayEnvironmentSource;
        if (_displayEnvironmentSource is not null)
        {
            _displayEnvironmentSource.DisplaysChanged += OnDisplaysChanged;
        }

        if (_settingsStore is not null)
        {
            _placementPersistence = new PlacementPersistence(
                SavePlacement,
                placementDebounceMilliseconds ?? 500);
        }

        if (host.SystemMetricsSource is { } source)
        {
            _metricsSampler = new PerformanceMetricsSampler(source, PublishMetrics, Settings.FastRefreshMilliseconds);
        }

        if (host.SlowMetricsSource is { } slowSource)
        {
            _slowMetricsSampler = new PerformanceSlowMetricsSampler(slowSource, PublishSlowMetrics, Settings.SlowRefreshMilliseconds);
        }
    }

    public StartupShellState State { get; private set; } = new(false, false, false, false, false);

    public PerformanceSettings Settings { get; private set; }

    public bool RecoveredInvalidSettingsOnStartup => _settingsStore?.RecoveredInvalidSettingsOnLastLoad ?? false;

    public StartupAccessDecision Start(StartupAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.HasExistingInstance)
        {
            if (context.IsReleaseBuild && context.IsElevationHandoff)
            {
                return context.IsElevated
                    ? StartupAccessDecision.HandOffToElevatedInstance
                    : StartupAccessDecision.RejectElevationHandoff;
            }

            return StartupAccessDecision.NotifyExistingInstanceAndExit;
        }

        if (context.IsReleaseBuild && !context.IsElevated && !context.ElevationAlreadyAttempted)
        {
            return StartupAccessDecision.StartCurrentInstanceAndAttemptElevation;
        }

        return StartupAccessDecision.StartCurrentInstance;
    }

    public void Start()
    {
        if (State.IsRunning)
        {
            return;
        }

        _host.ApplySettings(Settings);
        _host.ShowPerformanceBar(activate: false);
        RestorePlacementIfPortChanged();
        _host.CreateTrayIcon(OnTrayLeftClick, OnTrayRightClick);
        State = new(true, true, true, false, false);
        // 恢复期结算被 IsRunning 门控跳过，这里补结算以衔接任务栏行内守卫。
        SettlePlacement();
        StartMetricSampling();
        StartFullscreenWatcherIfEnabled();
    }

    public void OnFullscreenChanged(bool isFullscreen)
    {
        if (!State.IsRunning || isFullscreen == _isFullscreenActive)
        {
            return;
        }

        _isFullscreenActive = isFullscreen;
        if (isFullscreen)
        {
            if (Settings.AutoHideOnFullscreen &&
                !_isManuallyHidden &&
                State.IsPerformanceBarVisible)
            {
                _isAutoHiddenByFullscreen = true;
                SetPerformanceBarVisibilityCore(visible: false, activate: false);
            }

            return;
        }

        if (_isAutoHiddenByFullscreen)
        {
            _isAutoHiddenByFullscreen = false;
            SetPerformanceBarVisibilityCore(visible: true, activate: false);
        }
    }

    public void OnRepeatedLaunchRequested()
    {
        if (!State.IsRunning)
        {
            return;
        }

        // 重复启动唤起是明确的手动显示入口，清两种隐藏标记并遵守同全屏周期的抑制。
        _isManuallyHidden = false;
        _isAutoHiddenByFullscreen = false;

        var wasVisible = State.IsPerformanceBarVisible;
        _host.SetPerformanceBarVisible(visible: true, activate: true);
        RestorePlacementIfPortChanged();
        // 同显隐往返路径：重新结算行内归属并按需重启任务栏守卫。
        SettlePlacement();
        State = State with { IsPerformanceBarVisible = true };
        if (!wasVisible)
        {
            StartMetricSampling();
        }
    }

    public void SelectMenuItem(ShellMenuAction action)
    {
        if (!State.IsRunning)
        {
            return;
        }

        switch (action)
        {
            case ShellMenuAction.TogglePerformanceBarVisibility:
                SetPerformanceBarVisibility(!State.IsPerformanceBarVisible, activate: !State.IsPerformanceBarVisible);
                break;
            case ShellMenuAction.OpenSettings:
                // 重新打开取消待执行的回收；实例已释放时宿主按需重建。
                CancelSettingsIdleRecycle();
                _host.ShowSettingsWindow(Settings, UpdateSettings, RecoveredInvalidSettingsOnStartup);
                State = State with
                {
                    IsSettingsWindowCreated = true,
                    IsSettingsWindowVisible = true,
                    IsSettingsWindowRecyclePending = false
                };
                break;
            case ShellMenuAction.ToggleCenterInTaskbarRow:
                UpdateSettings(new SettingsPatch { CenterInTaskbarRow = !Settings.CenterInTaskbarRow });
                if (Settings.CenterInTaskbarRow)
                {
                    // 开启立即结算一次，卡片弹回行中心；关闭保持原地不动。
                    SettlePlacement();
                }

                break;
            case ShellMenuAction.ToggleTransparentDisplay:
                UpdateSettings(new SettingsPatch { TransparentDisplay = !Settings.TransparentDisplay });
                break;
            case ShellMenuAction.Exit:
                CancelSettingsIdleRecycle();
                StopMetricSampling();
                _placementPersistence?.Flush();
                StopFullscreenWatcher();
                _isAutoHiddenByFullscreen = false;
                _isManuallyHidden = false;
                State = new(false, false, false, false, false);
                _host.Shutdown();
                break;
        }
    }

    public PerformanceSettings UpdateSettings(SettingsPatch patch)
    {
        if (!State.IsRunning)
        {
            throw new InvalidOperationException("应用未运行时不能更改设置。");
        }

        var next = Settings.Apply(patch);
        if (patch.Autostart is { } requestedAutostart)
        {
            if (_autostartPort is not { } autostartPort)
            {
                // 端口不可用（Debug 构建等）时自启无法在系统中生效，按失败路径处理：
                // 不发布新 Settings、不持久化，与「失败不显示成功」语义一致。
                throw new InvalidOperationException("当前环境不支持开机自启，已保留原设置。");
            }

            var outcome = autostartPort.TrySetEnabled(requestedAutostart);
            if (outcome == AutostartRequestOutcome.Failed)
            {
                throw new InvalidOperationException("开机自启未在系统中生效，已保留原设置。");
            }

            // 自启状态必须反映系统实际结果，而不是请求值。
            next = next with { Autostart = outcome == AutostartRequestOutcome.Enabled };
        }

        next = next.WithWidget(LastPlacementSnapshot());
        _settingsStore?.Save(next);
        _placementPersistence?.ClearPending();

        var samplingIntervalChanged =
            Settings.FastRefreshMilliseconds != next.FastRefreshMilliseconds ||
            Settings.SlowRefreshMilliseconds != next.SlowRefreshMilliseconds;
        if (samplingIntervalChanged)
        {
            StopMetricSampling();
            _metricsSampler?.SetFastRefreshMilliseconds(next.FastRefreshMilliseconds);
            _slowMetricsSampler?.SetRefreshMilliseconds(next.SlowRefreshMilliseconds);
        }

        var autoHideOnFullscreenChanged = Settings.AutoHideOnFullscreen != next.AutoHideOnFullscreen;
        Settings = next;
        _host.ApplySettings(next);
        if (samplingIntervalChanged && CanSampleMetrics)
        {
            StartMetricSampling();
        }

        if (autoHideOnFullscreenChanged)
        {
            if (next.AutoHideOnFullscreen)
            {
                StartFullscreenWatcherIfEnabled();
            }
            else
            {
                StopFullscreenWatcher();
                // 关闭自动隐藏是显式修正：仅恢复因自动原因隐藏的状态，手动隐藏保持不变。
                if (_isAutoHiddenByFullscreen)
                {
                    _isAutoHiddenByFullscreen = false;
                    SetPerformanceBarVisibilityCore(visible: true, activate: false);
                }
            }
        }

        return next;
    }

    public void OnPerformanceBarRightClick()
    {
        ShowContextMenu(ShellMenuOrigin.PerformanceBar);
    }

    public void OnPerformanceBarNativeMoveRequested()
    {
        if (!State.IsRunning)
        {
            return;
        }

        _isPerformanceBarNativeMoveActive = true;
        StopMetricSampling();
        try
        {
            _host.BeginPerformanceBarNativeMove();
        }
        finally
        {
            _isPerformanceBarNativeMoveActive = false;
            SettlePlacement();
            if (CanSampleMetrics)
            {
                StartMetricSampling();
            }
        }
    }

    /// <summary>
    /// 性能条框架尺寸变化（内容渲染、字号调整等）后重新结算：
    /// 行内居中按实际宽度回中，其余情形结算幂等、不移动窗口。
    /// </summary>
    private void OnPerformanceBarFrameSizeChanged() => SettlePlacement();

    public void OnSettingsWindowClosed()
    {
        if (!State.IsRunning || !State.IsSettingsWindowVisible)
        {
            return;
        }

        _host.HideSettingsWindow();
        State = State with { IsSettingsWindowVisible = false, IsSettingsWindowRecyclePending = true };
        ScheduleSettingsIdleRecycle();
    }

    private void ScheduleSettingsIdleRecycle()
    {
        if (_settingsIdleRecycleDelay <= TimeSpan.Zero)
        {
            // 非正间隔表示禁用闲置回收，实例保留到退出。
            return;
        }

        _settingsRecycleTimer.Schedule(_settingsIdleRecycleDelay);
    }

    private void CancelSettingsIdleRecycle()
    {
        _settingsRecycleTimer.Cancel();
        if (State.IsSettingsWindowRecyclePending)
        {
            State = State with { IsSettingsWindowRecyclePending = false };
        }
    }

    private void OnSettingsIdleRecycleElapsed(object? sender, EventArgs e)
    {
        // 仅在仍处于「已创建且已隐藏」状态时释放；到期瞬间被打开则保留实例。
        if (!State.IsRunning || State.IsSettingsWindowVisible || !State.IsSettingsWindowCreated)
        {
            return;
        }

        _host.ReleaseSettingsWindow();
        State = State with { IsSettingsWindowCreated = false, IsSettingsWindowRecyclePending = false };
    }

    private void OnTrayLeftClick()
    {
        if (!State.IsRunning)
        {
            return;
        }

        SetPerformanceBarVisibility(!State.IsPerformanceBarVisible, activate: !State.IsPerformanceBarVisible);
    }

    private void SetPerformanceBarVisibility(bool visible, bool activate)
    {
        if (visible)
        {
            // 手动显示清两种隐藏标记；同一全屏周期内手动显示后保持显示。
            _isManuallyHidden = false;
            _isAutoHiddenByFullscreen = false;
        }
        else
        {
            // 手动隐藏优先于自动隐藏；退出全屏不得撤销用户的手动选择。
            _isManuallyHidden = true;
            _isAutoHiddenByFullscreen = false;
        }

        SetPerformanceBarVisibilityCore(visible, activate);
    }

    private void SetPerformanceBarVisibilityCore(bool visible, bool activate)
    {
        _host.SetPerformanceBarVisible(visible, activate: activate);
        State = State with { IsPerformanceBarVisible = visible };
        if (visible)
        {
            RestorePlacementIfPortChanged();
            // 重新结算行内归属：隐藏时守卫已停止，显示后必须按需重启，
            // 否则下一次任务栏遮挡将无人修复（显隐往返后守卫失效）。
            SettlePlacement();
            StartMetricSampling();
        }
        else
        {
            StopMetricSampling();
            StopTaskbarGuard();
        }
    }

    private void UpdateTaskbarGuard()
    {
        if (_taskbarGuard is null || _disposed)
        {
            return;
        }

        var wanted = _isInTaskbarRow && State.IsRunning;
        if (wanted == _isTaskbarGuardActive)
        {
            return;
        }

        _isTaskbarGuardActive = wanted;
        if (wanted)
        {
            _taskbarGuardFastTimer = new Timer(OnTaskbarGuardFastTick, null, TimeSpan.Zero, _taskbarGuardFastInterval);
            _taskbarGuardSlowTimer = new Timer(OnTaskbarGuardSlowTick, null, TimeSpan.Zero, _taskbarGuardSlowInterval);
        }
        else
        {
            StopTaskbarGuard();
        }
    }

    /// <summary>停止守卫并通知 adapter 把卡片恢复到普通 z 序层；可重复调用。</summary>
    private void StopTaskbarGuard()
    {
        _taskbarGuardFastTimer?.Dispose();
        _taskbarGuardFastTimer = null;
        _taskbarGuardSlowTimer?.Dispose();
        _taskbarGuardSlowTimer = null;
        if (_isTaskbarGuardActive)
        {
            _isTaskbarGuardActive = false;
            try
            {
                _taskbarGuard?.OnGuardStopped();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void OnTaskbarGuardFastTick(object? state) =>
        RunTaskbarGuardTick(static guard => guard.EnsureAboveTaskbar());

    private void OnTaskbarGuardSlowTick(object? state) =>
        RunTaskbarGuardTick(static guard => guard.RefreshTaskbarHandles());

    private void RunTaskbarGuardTick(Action<ITaskbarVisibilityGuardPort> tick)
    {
        // 隐藏、退出或原生拖动期间跳过；隐藏态由 StopTaskbarGuard 直接停表。
        if (_disposed || !State.IsRunning || !State.IsPerformanceBarVisible || _isPerformanceBarNativeMoveActive)
        {
            return;
        }

        try
        {
            if (_taskbarGuard is { } guard)
            {
                tick(guard);
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private WidgetPlacement LastPlacementSnapshot()
    {
        lock (_placementSync)
        {
            return _lastPlacement;
        }
    }

    /// <summary>
    /// 窗口首次创建或重建后，把待恢复位置写入窗口框架；
    /// 创建期的临时位置不参与保存，因此不会覆盖待恢复位置。
    /// </summary>
    private void RestorePlacementIfPortChanged()
    {
        if (_disposed)
        {
            return;
        }

        var port = _host.PerformanceBarPlacement;
        if (port is null || ReferenceEquals(port, _placementPort))
        {
            return;
        }

        _placementPort = port;
        var displays = _displayEnvironmentSource?.GetDisplays();
        if (displays is not { Count: > 0 })
        {
            return;
        }

        var stored = LastPlacementSnapshot();
        var frame = port.ReadFrame();
        var (restored, docked) = PlacementRules.RestorePlacement(
            stored,
            frame with { X = 0, Y = 0 },
            displays,
            PlacementInsets.Zero,
            Settings.CenterInTaskbarRow);
        port.SetFramePosition(restored.X, restored.Y);
        var placement = new WidgetPlacement
        {
            X = restored.X,
            Y = restored.Y,
            Docked = docked,
            InTaskbarRow = stored.InTaskbarRow
        };
        lock (_placementSync)
        {
            if (placement != _lastPlacement)
            {
                _lastPlacement = placement;
            }
        }

        if (placement != stored)
        {
            _placementPersistence?.Schedule();
        }

        // 恢复位置已写入框架，重新结算一次以衔接任务栏行内落点与守卫。
        SettlePlacement();
    }

    /// <summary>
    /// 读取原生实际框架，按卡片中心解析目标显示器后结算贴边并写回；
    /// 显示器消失时夹回可见工作区。重入门控防止摆窗触发的递归。
    /// </summary>
    private void SettlePlacement()
    {
        if (_disposed || !State.IsRunning || _isSettlingPlacement)
        {
            return;
        }

        var port = _placementPort ?? _host.PerformanceBarPlacement;
        if (port is null)
        {
            return;
        }

        var displays = _displayEnvironmentSource?.GetDisplays();
        if (displays is not { Count: > 0 })
        {
            return;
        }

        _isSettlingPlacement = true;
        try
        {
            var frame = port.ReadFrame();
            var centerDisplay = PlacementRules.FindDisplayByCenter(displays, frame);
            var target = centerDisplay ?? PlacementRules.FindNearestDisplay(displays, frame);
            if (target is not { } resolved)
            {
                return;
            }

            if (centerDisplay is null)
            {
                // 卡片中心不在任何显示器内（目标显示器消失等）：先夹回可见工作区。
                frame = PlacementRules.ClampIntoWorkArea(frame, resolved);
            }

            var (snapped, snappedDocked) = PlacementRules.SettleSnap(
                frame,
                resolved,
                LastPlacementSnapshot().Docked,
                PlacementInsets.Zero);
            // 行内落点：以结算前框架的中心与对应贴边落点判定（规格 F08）；
            // 不满足前提时回落普通贴边结算结果。
            var (rowSettled, rowDocked, inRow) = TaskbarRowRules.SettleRowPlacement(
                frame,
                resolved,
                snappedDocked,
                Settings.CenterInTaskbarRow);
            var (settled, docked) = inRow ? (rowSettled, rowDocked) : (snapped, snappedDocked);
            if (settled.X != frame.X || settled.Y != frame.Y)
            {
                port.SetFramePosition(settled.X, settled.Y);
            }

            var placement = new WidgetPlacement
            {
                X = settled.X,
                Y = settled.Y,
                Docked = docked,
                InTaskbarRow = inRow
            };
            _isInTaskbarRow = inRow;
            UpdateTaskbarGuard();
            lock (_placementSync)
            {
                if (placement == _lastPlacement)
                {
                    return;
                }

                _lastPlacement = placement;
            }

            _placementPersistence?.Schedule();
        }
        finally
        {
            _isSettlingPlacement = false;
        }
    }

    private void OnDisplaysChanged(object? sender, EventArgs eventArgs) => SettlePlacement();

    private void SavePlacement()
    {
        if (_settingsStore is null)
        {
            return;
        }

        _settingsStore.Save(Settings.WithWidget(LastPlacementSnapshot()));
    }

    private void OnTrayRightClick()
    {
        ShowContextMenu(ShellMenuOrigin.Tray);
    }

    private void ShowContextMenu(ShellMenuOrigin origin)
    {
        if (!State.IsRunning)
        {
            return;
        }

        var items = Array.AsReadOnly(
        [
            new ShellMenuItem(
                ShellMenuAction.TogglePerformanceBarVisibility,
                "显示/隐藏小窗",
                IsCheckable: true,
                IsChecked: State.IsPerformanceBarVisible),
            new ShellMenuItem(ShellMenuAction.OpenSettings, "打开设置"),
            new ShellMenuItem(
                ShellMenuAction.ToggleCenterInTaskbarRow,
                "任务栏内水平居中",
                IsCheckable: true,
                IsChecked: Settings.CenterInTaskbarRow),
            new ShellMenuItem(
                ShellMenuAction.ToggleTransparentDisplay,
                "透明显示",
                IsCheckable: true,
                IsChecked: Settings.TransparentDisplay),
            new ShellMenuItem(ShellMenuAction.Separator, string.Empty),
            new ShellMenuItem(ShellMenuAction.Exit, "退出")
        ]);
        _host.ShowContextMenu(origin, items, SelectMenuItem);
    }

    private void StartMetricSampling()
    {
        if (!CanSampleMetrics)
        {
            return;
        }

        if (_metricsSampler is null)
        {
            RunSlowOnlyMetricsSampling(static (sampler, generation) => sampler.Start(generation));
            return;
        }

        var currentGeneration = _metricsSampler.Start(SetMetricGeneration);
        _slowMetricsSampler?.Start(currentGeneration);
    }

    private bool CanSampleMetrics =>
        State.IsRunning && State.IsPerformanceBarVisible && !_isPerformanceBarNativeMoveActive;

    private void StartFullscreenWatcherIfEnabled()
    {
        if (!State.IsRunning || !Settings.AutoHideOnFullscreen ||
            _fullscreenWatcher is null || _isFullscreenWatcherRunning)
        {
            return;
        }

        _fullscreenWatcher.Start(OnFullscreenChanged);
        _isFullscreenWatcherRunning = true;
    }

    private void StopFullscreenWatcher()
    {
        if (!_isFullscreenWatcherRunning)
        {
            return;
        }

        _isFullscreenWatcherRunning = false;
        _isFullscreenActive = false;
        _fullscreenWatcher?.Stop();
    }

    private void StopMetricSampling()
    {
        if (_metricsSampler is null)
        {
            RunSlowOnlyMetricsSampling(static (sampler, generation) => sampler.Stop(generation));
            return;
        }

        var currentGeneration = _metricsSampler.Stop(SetMetricGeneration);
        _slowMetricsSampler?.Stop(currentGeneration);
    }

    /// <summary>仅有慢指标采样器时的启停：推进代数并交给慢采样器执行。</summary>
    private void RunSlowOnlyMetricsSampling(Action<PerformanceSlowMetricsSampler, long> run)
    {
        if (_slowMetricsSampler is null)
        {
            return;
        }

        var generation = Interlocked.Increment(ref _metricsGeneration);
        SetMetricGeneration(generation);
        run(_slowMetricsSampler, generation);
    }

    private void SetMetricGeneration(long generation)
    {
        Interlocked.Exchange(ref _metricsGeneration, generation);
        lock (_metricsSnapshotSync)
        {
            _latestMetricsSnapshot = new PerformanceMetricsSnapshot(
                generation,
                CpuPercentage: null,
                MemoryPercentage: null,
                MemoryUsedGiB: null,
                MemoryTotalGiB: null,
                DateTimeOffset.UtcNow);
        }

        _host.SetMetricGeneration(generation);
    }

    private void PublishMetrics(PerformanceMetricsSnapshot snapshot)
    {
        lock (_metricsSnapshotSync)
        {
            if (snapshot.Generation != Interlocked.Read(ref _metricsGeneration))
            {
                return;
            }

            var latest = _latestMetricsSnapshot ?? snapshot;
            latest = latest with
            {
                CpuPercentage = snapshot.CpuPercentage,
                MemoryPercentage = snapshot.MemoryPercentage,
                MemoryUsedGiB = snapshot.MemoryUsedGiB,
                MemoryTotalGiB = snapshot.MemoryTotalGiB,
                NetworkDownloadMegabytesPerSecond = snapshot.NetworkDownloadMegabytesPerSecond,
                NetworkUploadMegabytesPerSecond = snapshot.NetworkUploadMegabytesPerSecond,
                Timestamp = snapshot.Timestamp
            };
            _latestMetricsSnapshot = latest;
            _host.UpdatePerformanceMetrics(latest);
        }
    }

    private void PublishSlowMetrics(long generation, SlowMetricsReading reading)
    {
        lock (_metricsSnapshotSync)
        {
            if (generation != Interlocked.Read(ref _metricsGeneration))
            {
                return;
            }

            var latest = _latestMetricsSnapshot ?? new PerformanceMetricsSnapshot(
                generation,
                CpuPercentage: null,
                MemoryPercentage: null,
                MemoryUsedGiB: null,
                MemoryTotalGiB: null,
                DateTimeOffset.UtcNow);
            var gpu = reading.Gpu;
            latest = latest with
            {
                CpuTemperatureCelsius = reading.CpuTemperatureCelsius,
                GpuPercentage = gpu?.UtilizationPercentage,
                GpuMemoryPercentage = gpu?.MemoryUtilizationPercentage,
                GpuTemperatureCelsius = gpu?.TemperatureCelsius,
                Timestamp = DateTimeOffset.UtcNow
            };
            _latestMetricsSnapshot = latest;
            _host.UpdatePerformanceMetrics(latest);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopMetricSampling();
        StopTaskbarGuard();
        _placementPersistence?.Flush();
        _placementPersistence?.Dispose();
        if (_displayEnvironmentSource is not null)
        {
            _displayEnvironmentSource.DisplaysChanged -= OnDisplaysChanged;
        }

        StopFullscreenWatcher();
        _settingsRecycleTimer.Elapsed -= OnSettingsIdleRecycleElapsed;
        _settingsRecycleTimer.Cancel();
        _settingsRecycleTimer.Dispose();

        _metricsSampler?.Dispose();
        _slowMetricsSampler?.Dispose();
    }
}
