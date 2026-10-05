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
    bool IsSettingsWindowVisible);

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

public interface IStartupShellHost
{
    void SetPerformanceBarMoveRequestHandler(Action handler);

    void BeginPerformanceBarNativeMove();

    ISystemMetricsSource? SystemMetricsSource { get; }

    ISlowMetricsSource? SlowMetricsSource => null;

    /// <summary>显示器与工作区信息端口；缺省表示宿主不提供落位能力。</summary>
    IDisplayEnvironmentSource? DisplayEnvironmentSource => null;

    /// <summary>性能条窗口框架读写端口；窗口重建后宿主必须返回新实例。</summary>
    IPerformanceBarPlacementPort? PerformanceBarPlacement => null;

    void ShowPerformanceBar(bool activate);

    void SetPerformanceBarVisible(bool visible, bool activate);

    void CreateTrayIcon(Action leftClick, Action rightClick);

    void ShowContextMenu(
        ShellMenuOrigin origin,
        IReadOnlyList<ShellMenuItem> items,
        Action<ShellMenuAction> selectItem);

    void ShowSettingsWindow();

    ISettingsStore? SettingsStore => null;

    void ShowSettingsWindow(
        PerformanceSettings settings,
        Func<SettingsPatch, PerformanceSettings> updateSettings,
        bool recoveredInvalidSettings) => ShowSettingsWindow();

    void ApplySettings(PerformanceSettings settings)
    {
    }

    void HideSettingsWindow();

    void SetMetricGeneration(long generation);

    void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot);

    void Shutdown();
}

public sealed class StartupShellController : IDisposable
{
    private readonly IStartupShellHost _host;
    private readonly ISettingsStore? _settingsStore;
    private readonly PerformanceMetricsSampler? _metricsSampler;
    private readonly PerformanceSlowMetricsSampler? _slowMetricsSampler;
    private readonly IDisplayEnvironmentSource? _displayEnvironmentSource;
    private readonly PlacementPersistence? _placementPersistence;
    private readonly object _metricsSnapshotSync = new();
    private readonly object _placementSync = new();
    private PerformanceMetricsSnapshot? _latestMetricsSnapshot;
    private long _metricsGeneration;
    private bool _isPerformanceBarNativeMoveActive;
    private IPerformanceBarPlacementPort? _placementPort;
    private WidgetPlacement _lastPlacement = new();
    private bool _isSettlingPlacement;
    private bool _disposed;

    public StartupShellController(
        IStartupShellHost host,
        int? fastRefreshMilliseconds = null,
        int? slowRefreshMilliseconds = null,
        int? placementDebounceMilliseconds = null)
    {
        _host = host;
        _settingsStore = host.SettingsStore;
        var loadedSettings = (_settingsStore?.Load() ?? PerformanceSettings.Default).Validate();
        Settings = (loadedSettings with
        {
            FastRefreshMilliseconds = fastRefreshMilliseconds ?? loadedSettings.FastRefreshMilliseconds,
            SlowRefreshMilliseconds = slowRefreshMilliseconds ?? loadedSettings.SlowRefreshMilliseconds
        }).Validate();
        _host.SetPerformanceBarMoveRequestHandler(OnPerformanceBarNativeMoveRequested);
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
        StartMetricSampling();
    }

    public void OnRepeatedLaunchRequested()
    {
        if (!State.IsRunning)
        {
            return;
        }

        var wasVisible = State.IsPerformanceBarVisible;
        _host.SetPerformanceBarVisible(visible: true, activate: true);
        RestorePlacementIfPortChanged();
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
                _host.ShowSettingsWindow(Settings, UpdateSettings, RecoveredInvalidSettingsOnStartup);
                State = State with
                {
                    IsSettingsWindowCreated = true,
                    IsSettingsWindowVisible = true
                };
                break;
            case ShellMenuAction.ToggleCenterInTaskbarRow:
                UpdateSettings(new SettingsPatch { CenterInTaskbarRow = !Settings.CenterInTaskbarRow });
                break;
            case ShellMenuAction.ToggleTransparentDisplay:
                UpdateSettings(new SettingsPatch { TransparentDisplay = !Settings.TransparentDisplay });
                break;
            case ShellMenuAction.Exit:
                StopMetricSampling();
                _placementPersistence?.Flush();
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

        var next = Settings.Apply(patch).WithWidget(LastPlacementSnapshot());
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

        Settings = next;
        _host.ApplySettings(next);
        if (samplingIntervalChanged && CanSampleMetrics)
        {
            StartMetricSampling();
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

    public void OnSettingsWindowClosed()
    {
        if (!State.IsRunning || !State.IsSettingsWindowVisible)
        {
            return;
        }

        _host.HideSettingsWindow();
        State = State with { IsSettingsWindowVisible = false };
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
        _host.SetPerformanceBarVisible(visible, activate: activate);
        State = State with { IsPerformanceBarVisible = visible };
        if (visible)
        {
            RestorePlacementIfPortChanged();
            StartMetricSampling();
        }
        else
        {
            StopMetricSampling();
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
            PlacementInsets.Zero);
        port.SetFramePosition(restored.X, restored.Y);
        var placement = new WidgetPlacement { X = restored.X, Y = restored.Y, Docked = docked };
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

            var (settled, docked) = PlacementRules.SettleSnap(
                frame,
                resolved,
                LastPlacementSnapshot().Docked,
                PlacementInsets.Zero);
            if (settled.X != frame.X || settled.Y != frame.Y)
            {
                port.SetFramePosition(settled.X, settled.Y);
            }

            var placement = new WidgetPlacement { X = settled.X, Y = settled.Y, Docked = docked };
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
            if (_slowMetricsSampler is null)
            {
                return;
            }

            var generation = Interlocked.Increment(ref _metricsGeneration);
            SetMetricGeneration(generation);
            _slowMetricsSampler.Start(generation);
            return;
        }

        var currentGeneration = _metricsSampler.Start(SetMetricGeneration);
        _slowMetricsSampler?.Start(currentGeneration);
    }

    private bool CanSampleMetrics =>
        State.IsRunning && State.IsPerformanceBarVisible && !_isPerformanceBarNativeMoveActive;

    private void StopMetricSampling()
    {
        if (_metricsSampler is null)
        {
            if (_slowMetricsSampler is null)
            {
                return;
            }

            var generation = Interlocked.Increment(ref _metricsGeneration);
            SetMetricGeneration(generation);
            _slowMetricsSampler.Stop(generation);
            return;
        }

        var currentGeneration = _metricsSampler.Stop(SetMetricGeneration);
        _slowMetricsSampler?.Stop(currentGeneration);
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
        _placementPersistence?.Flush();
        _placementPersistence?.Dispose();
        if (_displayEnvironmentSource is not null)
        {
            _displayEnvironmentSource.DisplaysChanged -= OnDisplaysChanged;
        }

        _metricsSampler?.Dispose();
        _slowMetricsSampler?.Dispose();
    }
}
