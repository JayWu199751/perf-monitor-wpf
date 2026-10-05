using System.Threading.Channels;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

public sealed class PerformanceMetricsContractTests
{
    [Fact(DisplayName = "启动性能条后，首次 CPU 基线和内存百分比进入用户可见快照")]
    public async Task First_sample_publishes_cpu_baseline_and_memory_percentage()
    {
        var host = new MetricsShellHost(new SequenceSystemMetricsSource(
            [new CpuTimeCounters(KernelTime: 100, UserTime: 50, IdleTime: 80)],
            new PhysicalMemoryCounters(TotalPhysicalBytes: 8UL * 1024 * 1024 * 1024, AvailablePhysicalBytes: 3UL * 1024 * 1024 * 1024)));
        var shell = new StartupShellController(host);

        shell.Start();
        var snapshot = await host.ReadNextSnapshotAsync();
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(1, host.PerformanceBarShowCount);
        Assert.Equal(0, snapshot.CpuPercentage);
        Assert.Equal(63, snapshot.MemoryPercentage);
        Assert.Equal(5d, snapshot.MemoryUsedGiB);
        Assert.Equal(8d, snapshot.MemoryTotalGiB);
    }

    [Fact(DisplayName = "CPU 差分把 kernel 含 idle 的 75.5% 按 Math.round 语义取为 76%")]
    public async Task Cpu_usage_uses_kernel_delta_including_idle_and_rounds_midpoints_up()
    {
        var host = new MetricsShellHost(new SequenceSystemMetricsSource(
        [
            new CpuTimeCounters(KernelTime: 1000, UserTime: 200, IdleTime: 900),
            new CpuTimeCounters(KernelTime: 1150, UserTime: 250, IdleTime: 949)
        ],
        new PhysicalMemoryCounters(TotalPhysicalBytes: 8UL * 1024 * 1024 * 1024, AvailablePhysicalBytes: 3UL * 1024 * 1024 * 1024)));
        var shell = new StartupShellController(host);

        shell.Start();
        _ = await host.ReadNextSnapshotAsync();
        var snapshot = await host.ReadNextSnapshotAsync();
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(76, snapshot.CpuPercentage);
    }

    [Fact(DisplayName = "CPU 累计时间没有变化时显示零而不是 NaN")]
    public async Task Cpu_usage_returns_zero_when_the_difference_window_has_no_elapsed_time()
    {
        var reading = new CpuTimeCounters(KernelTime: 1000, UserTime: 200, IdleTime: 900);
        var host = new MetricsShellHost(new SequenceSystemMetricsSource(
            [reading, reading],
            new PhysicalMemoryCounters(TotalPhysicalBytes: 8UL * 1024 * 1024 * 1024, AvailablePhysicalBytes: 3UL * 1024 * 1024 * 1024)));
        var shell = new StartupShellController(host);

        shell.Start();
        _ = await host.ReadNextSnapshotAsync();
        var snapshot = await host.ReadNextSnapshotAsync();
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(0, snapshot.CpuPercentage);
    }

    [Fact(DisplayName = "内存使用率先按原始字节计算，GiB 只用于快照展示")]
    public async Task Memory_percentage_uses_unrounded_physical_bytes()
    {
        var host = new MetricsShellHost(new SequenceSystemMetricsSource(
            [new CpuTimeCounters(KernelTime: 100, UserTime: 50, IdleTime: 80)],
            new PhysicalMemoryCounters(TotalPhysicalBytes: 1_600_000_000, AvailablePhysicalBytes: 500_000_000)));
        var shell = new StartupShellController(host);

        shell.Start();
        var snapshot = await host.ReadNextSnapshotAsync();
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(69, snapshot.MemoryPercentage);
        Assert.NotNull(snapshot.MemoryUsedGiB);
        Assert.NotNull(snapshot.MemoryTotalGiB);
        Assert.Equal(1.0244548321, snapshot.MemoryUsedGiB.Value, precision: 10);
        Assert.Equal(1.4901161194, snapshot.MemoryTotalGiB.Value, precision: 10);
    }

    [Fact(DisplayName = "源读取缺失显示为缺失，CPU 恢复后先建立零基线")]
    public async Task Failed_sources_publish_missing_values_and_cpu_recovers_with_a_baseline()
    {
        var host = new MetricsShellHost(new SequenceSystemMetricsSource(
        [
            null,
            new CpuTimeCounters(KernelTime: 200, UserTime: 100, IdleTime: 170)
        ],
        memoryReading: null));
        var shell = new StartupShellController(host);

        shell.Start();
        var failed = await host.ReadNextSnapshotAsync();
        var recovered = await host.ReadNextSnapshotAsync();
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Null(failed.CpuPercentage);
        Assert.Null(failed.MemoryPercentage);
        Assert.Null(failed.MemoryUsedGiB);
        Assert.Null(failed.MemoryTotalGiB);
        Assert.Equal(0, recovered.CpuPercentage);
        Assert.Null(recovered.MemoryPercentage);
    }

    [Fact(DisplayName = "隐藏再显示会重建采样基线，旧代际在途读数不能覆盖新快照")]
    public async Task Hiding_and_showing_the_bar_discards_an_earlier_in_flight_generation()
    {
        var source = new BlockingSystemMetricsSource();
        var host = new MetricsShellHost(source);
        var shell = new StartupShellController(host);
        shell.Start();

        await source.FirstCpuReadStarted.WaitAsync(TimeSpan.FromSeconds(3));
        var oldGeneration = host.CurrentGeneration;
        host.ClickTrayLeft();
        host.ClickTrayLeft();
        var newGeneration = host.CurrentGeneration;
        source.ReleaseFirstCpuRead();

        var snapshot = await host.ReadNextSnapshotAsync();
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.True(newGeneration > oldGeneration);
        Assert.Equal(newGeneration, snapshot.Generation);
        Assert.Equal(0, snapshot.CpuPercentage);
        Assert.Equal(2, source.CpuReadCount);
        Assert.Equal(1, source.MaximumConcurrentCpuReads);
    }

    [Theory(DisplayName = "快通道可选择规格支持的刷新周期")]
    [InlineData(1000)]
    [InlineData(2000)]
    [InlineData(5000)]
    public void Fast_sampling_accepts_each_supported_interval(int milliseconds)
    {
        var host = new MetricsShellHost(new SequenceSystemMetricsSource([], memoryReading: null));

        var shell = new StartupShellController(host, milliseconds);

        shell.Start();
        shell.SelectMenuItem(ShellMenuAction.Exit);
    }

    [Fact(DisplayName = "快通道拒绝规格以外的刷新周期")]
    public void Fast_sampling_rejects_unsupported_intervals()
    {
        var host = new MetricsShellHost(new SequenceSystemMetricsSource([], memoryReading: null));

        Assert.Throws<ArgumentOutOfRangeException>(() => new StartupShellController(host, 1500));
    }

    private sealed class MetricsShellHost(ISystemMetricsSource source) : IStartupShellHost
    {
        private readonly Channel<PerformanceMetricsSnapshot> _snapshots = Channel.CreateUnbounded<PerformanceMetricsSnapshot>();
        private Action? _trayLeftClick;
        private long _currentGeneration;

        public ISystemMetricsSource? SystemMetricsSource { get; } = source;

        public void SetPerformanceBarMoveRequestHandler(Action handler)
        {
        }

        public void BeginPerformanceBarNativeMove()
        {
        }

        public int PerformanceBarShowCount { get; private set; }

        public long CurrentGeneration => Interlocked.Read(ref _currentGeneration);

        public void ShowPerformanceBar(bool activate) => PerformanceBarShowCount++;

        public void SetPerformanceBarVisible(bool visible, bool activate)
        {
        }

        public void CreateTrayIcon(Action leftClick, Action rightClick) => _trayLeftClick = leftClick;

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

        public void SetMetricGeneration(long generation) => Interlocked.Exchange(ref _currentGeneration, generation);

        public void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot)
        {
            if (snapshot.Generation == CurrentGeneration)
            {
                _snapshots.Writer.TryWrite(snapshot);
            }
        }

        public void Shutdown()
        {
        }

        public void ClickTrayLeft() => _trayLeftClick?.Invoke();

        public async Task<PerformanceMetricsSnapshot> ReadNextSnapshotAsync() =>
            await _snapshots.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    private sealed class SequenceSystemMetricsSource(
        IEnumerable<CpuTimeCounters?> cpuReadings,
        PhysicalMemoryCounters? memoryReading) : ISystemMetricsSource
    {
        private readonly Queue<CpuTimeCounters?> _cpuReadings = new(cpuReadings);

        public CpuTimeCounters? ReadCpuTimes() =>
            _cpuReadings.Count > 0 ? _cpuReadings.Dequeue() : null;

        public PhysicalMemoryCounters? ReadPhysicalMemory() => memoryReading;
    }

    private sealed class BlockingSystemMetricsSource : ISystemMetricsSource
    {
        private readonly TaskCompletionSource _firstCpuReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _releaseFirstCpuRead = new(initialState: false);
        private int _cpuReadCount;
        private int _activeCpuReads;
        private int _maximumConcurrentCpuReads;

        public Task FirstCpuReadStarted => _firstCpuReadStarted.Task;

        public int CpuReadCount => Volatile.Read(ref _cpuReadCount);

        public int MaximumConcurrentCpuReads => Volatile.Read(ref _maximumConcurrentCpuReads);

        public CpuTimeCounters? ReadCpuTimes()
        {
            var active = Interlocked.Increment(ref _activeCpuReads);
            UpdateMaximum(active);
            var call = Interlocked.Increment(ref _cpuReadCount);
            try
            {
                if (call == 1)
                {
                    _firstCpuReadStarted.TrySetResult();
                    _releaseFirstCpuRead.Wait();
                    return new CpuTimeCounters(KernelTime: 100, UserTime: 40, IdleTime: 90);
                }

                return new CpuTimeCounters(KernelTime: 200, UserTime: 100, IdleTime: 170);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCpuReads);
            }
        }

        public PhysicalMemoryCounters? ReadPhysicalMemory() =>
            new(TotalPhysicalBytes: 4UL * 1024 * 1024 * 1024, AvailablePhysicalBytes: 2UL * 1024 * 1024 * 1024);

        public void ReleaseFirstCpuRead() => _releaseFirstCpuRead.Set();

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumConcurrentCpuReads);
                if (value <= current || Interlocked.CompareExchange(ref _maximumConcurrentCpuReads, value, current) == current)
                {
                    return;
                }
            }
        }
    }
}
