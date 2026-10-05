using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Core.Shell;

public enum ShellMenuOrigin
{
    PerformanceBar,
    Tray
}

public enum ShellMenuAction
{
    OpenSettings,
    Exit
}

public sealed record ShellMenuItem(ShellMenuAction Action, string Label);

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

    void ShowPerformanceBar(bool activate);

    void SetPerformanceBarVisible(bool visible, bool activate);

    void CreateTrayIcon(Action leftClick, Action rightClick);

    void ShowContextMenu(
        ShellMenuOrigin origin,
        IReadOnlyList<ShellMenuItem> items,
        Action<ShellMenuAction> selectItem);

    void ShowSettingsWindow();

    void HideSettingsWindow();

    void SetMetricGeneration(long generation);

    void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot);

    void Shutdown();
}

public sealed class StartupShellController : IDisposable
{
    private static readonly IReadOnlyList<ShellMenuItem> BasicMenuItems = Array.AsReadOnly(
    [
        new ShellMenuItem(ShellMenuAction.OpenSettings, "打开设置"),
        new ShellMenuItem(ShellMenuAction.Exit, "退出")
    ]);

    private readonly IStartupShellHost _host;
    private readonly PerformanceMetricsSampler? _metricsSampler;
    private readonly PerformanceSlowMetricsSampler? _slowMetricsSampler;
    private readonly object _metricsSnapshotSync = new();
    private PerformanceMetricsSnapshot? _latestMetricsSnapshot;
    private long _metricsGeneration;
    private bool _disposed;

    public StartupShellController(
        IStartupShellHost host,
        int fastRefreshMilliseconds = PerformanceMetricsSampler.DefaultFastRefreshMilliseconds,
        int slowRefreshMilliseconds = PerformanceSlowMetricsSampler.DefaultRefreshMilliseconds)
    {
        _host = host;
        _host.SetPerformanceBarMoveRequestHandler(OnPerformanceBarNativeMoveRequested);
        if (host.SystemMetricsSource is { } source)
        {
            _metricsSampler = new PerformanceMetricsSampler(source, PublishMetrics, fastRefreshMilliseconds);
        }

        if (host.SlowMetricsSource is { } slowSource)
        {
            _slowMetricsSampler = new PerformanceSlowMetricsSampler(slowSource, PublishSlowMetrics, slowRefreshMilliseconds);
        }
    }

    public StartupShellState State { get; private set; } = new(false, false, false, false, false);

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

        _host.ShowPerformanceBar(activate: false);
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

        _host.SetPerformanceBarVisible(visible: true, activate: true);
        State = State with { IsPerformanceBarVisible = true };
    }

    public void SelectMenuItem(ShellMenuAction action)
    {
        if (!State.IsRunning)
        {
            return;
        }

        switch (action)
        {
            case ShellMenuAction.OpenSettings:
                _host.ShowSettingsWindow();
                State = State with
                {
                    IsSettingsWindowCreated = true,
                    IsSettingsWindowVisible = true
                };
                break;
            case ShellMenuAction.Exit:
                StopMetricSampling();
                State = new(false, false, false, false, false);
                _host.Shutdown();
                break;
        }
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

        _host.BeginPerformanceBarNativeMove();
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

        var visible = !State.IsPerformanceBarVisible;
        _host.SetPerformanceBarVisible(visible, activate: visible);
        State = State with { IsPerformanceBarVisible = visible };
        if (visible)
        {
            StartMetricSampling();
        }
        else
        {
            StopMetricSampling();
        }
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

        _host.ShowContextMenu(origin, BasicMenuItems, SelectMenuItem);
    }

    private void StartMetricSampling()
    {
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
        _metricsSampler?.Dispose();
        _slowMetricsSampler?.Dispose();
    }
}
