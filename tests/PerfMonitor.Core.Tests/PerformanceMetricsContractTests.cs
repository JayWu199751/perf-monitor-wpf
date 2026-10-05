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

    [Fact(DisplayName = "活动处理器组变化时本轮 CPU 缺失，并以新组集合重建差分基线")]
    public async Task Cpu_usage_discards_a_delta_when_the_processor_group_count_changes()
    {
        var host = new MetricsShellHost(new SequenceSystemMetricsSource(
        [
            new CpuTimeCounters(KernelTime: 1000, UserTime: 100, IdleTime: 900, ProcessorGroupCount: 1),
            new CpuTimeCounters(KernelTime: 1500, UserTime: 100, IdleTime: 1300, ProcessorGroupCount: 2),
            new CpuTimeCounters(KernelTime: 1535, UserTime: 115, IdleTime: 1320, ProcessorGroupCount: 2)
        ],
        new PhysicalMemoryCounters(TotalPhysicalBytes: 8UL * 1024 * 1024 * 1024, AvailablePhysicalBytes: 3UL * 1024 * 1024 * 1024)));
        var shell = new StartupShellController(host);

        shell.Start();
        var first = await host.ReadNextSnapshotAsync();
        var topologyChanged = await host.ReadNextSnapshotAsync();
        var stableTopology = await host.ReadNextSnapshotAsync();
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(0, first.CpuPercentage);
        Assert.Null(topologyChanged.CpuPercentage);
        Assert.Equal(60, stableTopology.CpuPercentage);
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

    [Fact(DisplayName = "慢通道首轮立即采样并将 GPU 与 ACPI 温度并入快照，不覆盖快通道读数")]
    public async Task Slow_metrics_are_merged_into_the_user_visible_snapshot()
    {
        var slowSource = new SequenceSlowMetricsSource(
            new GpuMetricsReading(UtilizationPercentage: 41, MemoryUtilizationPercentage: 52, TemperatureCelsius: 63),
            cpuTemperatureCelsius: 38,
            subsequentGpuReading: new GpuMetricsReading(UtilizationPercentage: 44, MemoryUtilizationPercentage: 55, TemperatureCelsius: 66));
        var host = new MetricsShellHost(
            new SequenceSystemMetricsSource(
                Enumerable.Repeat<CpuTimeCounters?>(
                    new CpuTimeCounters(KernelTime: 100, UserTime: 50, IdleTime: 80),
                    count: 10),
                new PhysicalMemoryCounters(TotalPhysicalBytes: 8UL * 1024 * 1024 * 1024, AvailablePhysicalBytes: 3UL * 1024 * 1024 * 1024),
                networkReadingFactory: sequence => new NetworkCountersSnapshot(
                    TimeSpan.FromSeconds(sequence),
                    [new NetworkInterfaceCounters(
                        InterfaceLuid: 1,
                        IsUp: true,
                        IsLoopback: false,
                        PhysicalMediumType: 14,
                        InOctets: (ulong)sequence * 1024 * 1024,
                        OutOctets: (ulong)sequence * 2 * 1024 * 1024)])),
            slowSource);
        var shell = new StartupShellController(host, slowRefreshMilliseconds: 3000);

        shell.Start();
        var firstSlowSnapshot = await host.ReadSnapshotUntilAsync(value =>
            value.GpuPercentage is not null && value.CpuTemperatureCelsius is not null);
        var fastSnapshot = await host.ReadSnapshotUntilAsync(value =>
            value.GpuPercentage == 41 &&
            value.CpuTemperatureCelsius == 38 &&
            value.NetworkDownloadMegabytesPerSecond == 1d &&
            value.NetworkUploadMegabytesPerSecond == 2d);
        var nextSlowSnapshot = await host.ReadSnapshotUntilAsync(value => value.GpuPercentage == 44);
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(41, firstSlowSnapshot.GpuPercentage);
        Assert.Equal(52, firstSlowSnapshot.GpuMemoryPercentage);
        Assert.Equal(63, firstSlowSnapshot.GpuTemperatureCelsius);
        Assert.Equal(38, firstSlowSnapshot.CpuTemperatureCelsius);
        Assert.Equal(0, fastSnapshot.CpuPercentage);
        Assert.Equal(63, fastSnapshot.MemoryPercentage);
        Assert.Equal(41, fastSnapshot.GpuPercentage);
        Assert.Equal(38, fastSnapshot.CpuTemperatureCelsius);
        Assert.Equal(1d, fastSnapshot.NetworkDownloadMegabytesPerSecond);
        Assert.Equal(2d, fastSnapshot.NetworkUploadMegabytesPerSecond);
        Assert.Equal(44, nextSlowSnapshot.GpuPercentage);
        Assert.Equal(55, nextSlowSnapshot.GpuMemoryPercentage);
        Assert.Equal(66, nextSlowSnapshot.GpuTemperatureCelsius);
        Assert.Equal(38, nextSlowSnapshot.CpuTemperatureCelsius);
        Assert.Equal(1d, nextSlowSnapshot.NetworkDownloadMegabytesPerSecond);
        Assert.Equal(2d, nextSlowSnapshot.NetworkUploadMegabytesPerSecond);
        Assert.True(slowSource.GpuReadCount >= 2);
        Assert.True(slowSource.TemperatureReadCount >= 2);
    }

    [Fact(DisplayName = "GPU 读取失败时 ACPI 温度与 CPU、内存仍进入用户可见快照")]
    public async Task Slow_gpu_failure_does_not_interrupt_temperature_or_fast_metrics()
    {
        var slowSource = new FailingGpuSlowMetricsSource(cpuTemperatureCelsius: 44);
        var host = new MetricsShellHost(
            new SequenceSystemMetricsSource(
                [new CpuTimeCounters(KernelTime: 100, UserTime: 50, IdleTime: 80)],
                new PhysicalMemoryCounters(TotalPhysicalBytes: 8UL * 1024 * 1024 * 1024, AvailablePhysicalBytes: 3UL * 1024 * 1024 * 1024)),
            slowSource);
        var shell = new StartupShellController(host);

        shell.Start();
        var snapshot = await host.ReadSnapshotUntilAsync(value => value.CpuTemperatureCelsius is not null);
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Null(snapshot.GpuPercentage);
        Assert.Null(snapshot.GpuMemoryPercentage);
        Assert.Null(snapshot.GpuTemperatureCelsius);
        Assert.Equal(44, snapshot.CpuTemperatureCelsius);
        Assert.Equal(0, snapshot.CpuPercentage);
        Assert.Equal(63, snapshot.MemoryPercentage);
        Assert.Equal(1, slowSource.GpuReadCount);
    }

    [Fact(DisplayName = "ACPI 温度读取失败时 GPU 三项与 CPU、内存仍进入用户可见快照")]
    public async Task Slow_temperature_failure_does_not_interrupt_gpu_or_fast_metrics()
    {
        var slowSource = new FailingTemperatureSlowMetricsSource(
            new GpuMetricsReading(UtilizationPercentage: 31, MemoryUtilizationPercentage: 42, TemperatureCelsius: 57));
        var host = new MetricsShellHost(
            new SequenceSystemMetricsSource(
                [new CpuTimeCounters(KernelTime: 100, UserTime: 50, IdleTime: 80)],
                new PhysicalMemoryCounters(TotalPhysicalBytes: 8UL * 1024 * 1024 * 1024, AvailablePhysicalBytes: 3UL * 1024 * 1024 * 1024)),
            slowSource);
        var shell = new StartupShellController(host);

        shell.Start();
        var snapshot = await host.ReadSnapshotUntilAsync(value => value.GpuPercentage is not null);
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(31, snapshot.GpuPercentage);
        Assert.Equal(42, snapshot.GpuMemoryPercentage);
        Assert.Equal(57, snapshot.GpuTemperatureCelsius);
        Assert.Null(snapshot.CpuTemperatureCelsius);
        Assert.Equal(0, snapshot.CpuPercentage);
        Assert.Equal(63, snapshot.MemoryPercentage);
    }

    [Fact(DisplayName = "停止与重启期间不重叠慢通道读取，旧代际迟到结果不覆盖新快照")]
    public async Task Slow_metrics_do_not_overlap_or_publish_a_late_generation()
    {
        var slowSource = new BlockingSlowMetricsSource();
        var host = new MetricsShellHost(
            new SequenceSystemMetricsSource([], memoryReading: null),
            slowSource);
        var shell = new StartupShellController(host);

        shell.Start();
        await slowSource.FirstGpuReadStarted.WaitAsync(TimeSpan.FromSeconds(3));
        var oldGeneration = host.CurrentGeneration;
        host.ClickTrayLeft();
        Assert.True(slowSource.FirstGpuCancellationToken.IsCancellationRequested);
        host.ClickTrayLeft();
        var newGeneration = host.CurrentGeneration;
        await Task.Delay(100);
        Assert.Equal(1, slowSource.GpuReadCount);

        slowSource.ReleaseFirstGpuRead();
        var snapshot = await host.ReadSnapshotUntilAsync(value => value.GpuPercentage is not null);
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.True(newGeneration > oldGeneration);
        Assert.Equal(newGeneration, snapshot.Generation);
        Assert.Equal(22, snapshot.GpuPercentage);
        Assert.Equal(1, slowSource.MaximumConcurrentGpuReads);
        Assert.Equal(2, slowSource.GpuReadCount);
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

    [Fact(DisplayName = "采样暂停恢复后，慢通道首轮发布保留快通道旧读数")]
    public async Task Resuming_sampling_keeps_previous_fast_readings_instead_of_missing_values()
    {
        var slowSource = new BlockingSlowMetricsSource();
        var host = new MetricsShellHost(
            new SequenceSystemMetricsSource(
                Enumerable.Repeat<CpuTimeCounters?>(
                    new CpuTimeCounters(KernelTime: 100, UserTime: 50, IdleTime: 80),
                    count: 100),
                new PhysicalMemoryCounters(TotalPhysicalBytes: 8UL * 1024 * 1024 * 1024, AvailablePhysicalBytes: 3UL * 1024 * 1024 * 1024)),
            slowSource);
        var shell = new StartupShellController(host);

        shell.Start();
        await slowSource.FirstGpuReadStarted.WaitAsync(TimeSpan.FromSeconds(3));
        var fastSnapshot = await host.ReadSnapshotUntilAsync(value => value.CpuPercentage is not null);
        host.ClickTrayLeft();
        host.ClickTrayLeft();
        // 先放行被取消代际阻塞的首轮读取，让出慢通道闸门，新代际读取才能继续。
        slowSource.ReleaseFirstGpuRead();
        var resumedSnapshot = await host.ReadSnapshotUntilAsync(value => value.GpuPercentage == 22);
        shell.SelectMenuItem(ShellMenuAction.Exit);

        Assert.Equal(0, fastSnapshot.CpuPercentage);
        Assert.Equal(63, fastSnapshot.MemoryPercentage);
        Assert.Equal(22, resumedSnapshot.GpuPercentage);
        Assert.Equal(0, resumedSnapshot.CpuPercentage);
        Assert.Equal(63, resumedSnapshot.MemoryPercentage);
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

    [Theory(DisplayName = "慢通道接受规格支持的刷新周期")]
    [InlineData(3000)]
    [InlineData(5000)]
    public void Slow_sampling_accepts_each_supported_interval(int milliseconds)
    {
        var host = new MetricsShellHost(
            new SequenceSystemMetricsSource([], memoryReading: null),
            new SequenceSlowMetricsSource(gpuReading: null, cpuTemperatureCelsius: null));

        var shell = new StartupShellController(host, slowRefreshMilliseconds: milliseconds);
        shell.Start();
        shell.SelectMenuItem(ShellMenuAction.Exit);
    }

    [Fact(DisplayName = "慢通道拒绝规格以外的刷新周期")]
    public void Slow_sampling_rejects_unsupported_intervals()
    {
        var host = new MetricsShellHost(
            new SequenceSystemMetricsSource([], memoryReading: null),
            new SequenceSlowMetricsSource(gpuReading: null, cpuTemperatureCelsius: null));

        Assert.Throws<ArgumentOutOfRangeException>(() => new StartupShellController(host, slowRefreshMilliseconds: 4000));
    }

    private sealed class MetricsShellHost(
        ISystemMetricsSource source,
        ISlowMetricsSource? slowSource = null) : IStartupShellHost
    {
        private readonly Channel<PerformanceMetricsSnapshot> _snapshots = Channel.CreateUnbounded<PerformanceMetricsSnapshot>();
        private Action? _trayLeftClick;
        private long _currentGeneration;

        public ISystemMetricsSource? SystemMetricsSource { get; } = source;

        public ISlowMetricsSource? SlowMetricsSource { get; } = slowSource;

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

        public async Task<PerformanceMetricsSnapshot> ReadSnapshotUntilAsync(
            Func<PerformanceMetricsSnapshot, bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (true)
            {
                var snapshot = await _snapshots.Reader.ReadAsync(timeout.Token);
                if (predicate(snapshot))
                {
                    return snapshot;
                }
            }
        }
    }

    private sealed class SequenceSystemMetricsSource(
        IEnumerable<CpuTimeCounters?> cpuReadings,
        PhysicalMemoryCounters? memoryReading,
        Func<int, NetworkCountersSnapshot?>? networkReadingFactory = null) : ISystemMetricsSource
    {
        private readonly Queue<CpuTimeCounters?> _cpuReadings = new(cpuReadings);
        private int _networkReadCount;

        public CpuTimeCounters? ReadCpuTimes() =>
            _cpuReadings.Count > 0 ? _cpuReadings.Dequeue() : null;

        public PhysicalMemoryCounters? ReadPhysicalMemory() => memoryReading;

        public NetworkCountersSnapshot? ReadNetworkCounters() =>
            networkReadingFactory?.Invoke(Interlocked.Increment(ref _networkReadCount));
    }

    private sealed class SequenceSlowMetricsSource(
        GpuMetricsReading? gpuReading,
        int? cpuTemperatureCelsius,
        GpuMetricsReading? subsequentGpuReading = null) : ISlowMetricsSource
    {
        private int _gpuReadCount;
        private int _temperatureReadCount;

        public int GpuReadCount => Volatile.Read(ref _gpuReadCount);

        public int TemperatureReadCount => Volatile.Read(ref _temperatureReadCount);

        public Task<GpuMetricsReading?> ReadGpuMetricsAsync(CancellationToken cancellationToken)
        {
            var readCount = Interlocked.Increment(ref _gpuReadCount);
            return Task.FromResult(readCount > 1 && subsequentGpuReading is not null
                ? subsequentGpuReading
                : gpuReading);
        }

        public Task<int?> ReadCpuTemperatureCelsiusAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _temperatureReadCount);
            return Task.FromResult(cpuTemperatureCelsius);
        }
    }

    private sealed class FailingGpuSlowMetricsSource(int? cpuTemperatureCelsius) : ISlowMetricsSource
    {
        private int _gpuReadCount;

        public int GpuReadCount => Volatile.Read(ref _gpuReadCount);

        public Task<GpuMetricsReading?> ReadGpuMetricsAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _gpuReadCount);
            throw new InvalidOperationException("模拟 nvidia-smi 不可用。");
        }

        public Task<int?> ReadCpuTemperatureCelsiusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(cpuTemperatureCelsius);
    }

    private sealed class FailingTemperatureSlowMetricsSource(GpuMetricsReading? gpuReading) : ISlowMetricsSource
    {
        public Task<GpuMetricsReading?> ReadGpuMetricsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(gpuReading);

        public Task<int?> ReadCpuTemperatureCelsiusAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("模拟 ACPI/WMI 无数据或访问失败。");
    }

    private sealed class BlockingSlowMetricsSource : ISlowMetricsSource
    {
        private readonly TaskCompletionSource _firstGpuReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstGpuRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _gpuReadCount;
        private int _activeGpuReads;
        private int _maximumConcurrentGpuReads;
        private CancellationToken _firstGpuCancellationToken;

        public Task FirstGpuReadStarted => _firstGpuReadStarted.Task;

        public int GpuReadCount => Volatile.Read(ref _gpuReadCount);

        public int MaximumConcurrentGpuReads => Volatile.Read(ref _maximumConcurrentGpuReads);

        public CancellationToken FirstGpuCancellationToken => _firstGpuCancellationToken;

        public async Task<GpuMetricsReading?> ReadGpuMetricsAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeGpuReads);
            UpdateMaximum(active);
            var call = Interlocked.Increment(ref _gpuReadCount);
            try
            {
                if (call == 1)
                {
                    _firstGpuCancellationToken = cancellationToken;
                    _firstGpuReadStarted.TrySetResult();
                    await _releaseFirstGpuRead.Task.ConfigureAwait(false);
                }

                return new GpuMetricsReading(call == 1 ? 11 : 22, 30, 50);
            }
            finally
            {
                Interlocked.Decrement(ref _activeGpuReads);
            }
        }

        public Task<int?> ReadCpuTemperatureCelsiusAsync(CancellationToken cancellationToken) =>
            Task.FromResult<int?>(null);

        public void ReleaseFirstGpuRead() => _releaseFirstGpuRead.TrySetResult();

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximumConcurrentGpuReads);
                if (value <= current || Interlocked.CompareExchange(ref _maximumConcurrentGpuReads, value, current) == current)
                {
                    return;
                }
            }
        }
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

        public NetworkCountersSnapshot? ReadNetworkCounters() => null;

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
