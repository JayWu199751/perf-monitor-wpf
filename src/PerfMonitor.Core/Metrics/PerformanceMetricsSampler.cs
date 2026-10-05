namespace PerfMonitor.Core.Metrics;

public readonly record struct CpuTimeCounters(ulong KernelTime, ulong UserTime, ulong IdleTime);

public readonly record struct PhysicalMemoryCounters(ulong TotalPhysicalBytes, ulong AvailablePhysicalBytes);

public readonly record struct NetworkInterfaceCounters(
    ulong InterfaceLuid,
    bool IsUp,
    bool IsLoopback,
    uint PhysicalMediumType,
    ulong InOctets,
    ulong OutOctets);

public sealed record NetworkCountersSnapshot(
    TimeSpan MonotonicTimestamp,
    IReadOnlyList<NetworkInterfaceCounters> Interfaces);

public sealed record PerformanceMetricsSnapshot(
    long Generation,
    int? CpuPercentage,
    int? MemoryPercentage,
    double? MemoryUsedGiB,
    double? MemoryTotalGiB,
    DateTimeOffset Timestamp,
    double? NetworkDownloadMegabytesPerSecond = null,
    double? NetworkUploadMegabytesPerSecond = null);

public interface ISystemMetricsSource
{
    CpuTimeCounters? ReadCpuTimes();

    PhysicalMemoryCounters? ReadPhysicalMemory();

    NetworkCountersSnapshot? ReadNetworkCounters();
}

internal sealed class PerformanceMetricsSampler : IDisposable
{
    internal const int DefaultFastRefreshMilliseconds = 1000;
    private const ulong BytesPerGiB = 1024UL * 1024 * 1024;
    private static readonly int[] AllowedRefreshIntervals = [1000, 2000, 5000];

    private readonly object _sync = new();
    private readonly SemaphoreSlim _samplingGate = new(1, 1);
    private readonly ISystemMetricsSource _source;
    private readonly Action<PerformanceMetricsSnapshot> _publish;
    private readonly int _fastRefreshMilliseconds;
    private CancellationTokenSource? _activeGeneration;
    private long _generation;
    private bool _disposed;

    public PerformanceMetricsSampler(
        ISystemMetricsSource source,
        Action<PerformanceMetricsSnapshot> publish,
        int fastRefreshMilliseconds = DefaultFastRefreshMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(publish);

        if (!AllowedRefreshIntervals.Contains(fastRefreshMilliseconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fastRefreshMilliseconds),
                "快通道刷新间隔只能是 1000、2000 或 5000 毫秒。");
        }

        _source = source;
        _publish = publish;
        _fastRefreshMilliseconds = fastRefreshMilliseconds;
    }

    public int FastRefreshMilliseconds => _fastRefreshMilliseconds;

    public long Start(Action<long> setGeneration)
    {
        ArgumentNullException.ThrowIfNull(setGeneration);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_activeGeneration is not null)
            {
                return _generation;
            }

            var generation = ++_generation;
            var cancellation = new CancellationTokenSource();
            _activeGeneration = cancellation;
            setGeneration(generation);
            _ = Task.Run(() => SampleGenerationAsync(generation, cancellation));
            return generation;
        }
    }

    public long Stop(Action<long> setGeneration)
    {
        ArgumentNullException.ThrowIfNull(setGeneration);
        CancellationTokenSource? cancellation;
        long generation;
        lock (_sync)
        {
            generation = ++_generation;
            cancellation = _activeGeneration;
            _activeGeneration = null;
            setGeneration(generation);
        }

        cancellation?.Cancel();
        return generation;
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ++_generation;
            cancellation = _activeGeneration;
            _activeGeneration = null;
        }

        cancellation?.Cancel();
    }

    private async Task SampleGenerationAsync(long generation, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var cpuUsage = new CpuUsageTracker();
        var networkUsage = new NetworkThroughputTracker();

        try
        {
            while (!token.IsCancellationRequested)
            {
                await _samplingGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!IsCurrentGeneration(generation, cancellation, token))
                    {
                        return;
                    }

                    var cpuPercentage = cpuUsage.ReadPercentage(ReadCpuTimesSafely());
                    if (token.IsCancellationRequested || !IsCurrentGeneration(generation, cancellation, token))
                    {
                        return;
                    }

                    var memory = ReadMemorySafely();
                    if (token.IsCancellationRequested || !IsCurrentGeneration(generation, cancellation, token))
                    {
                        return;
                    }

                    var network = networkUsage.Read(ReadNetworkCountersSafely());
                    if (token.IsCancellationRequested || !IsCurrentGeneration(generation, cancellation, token))
                    {
                        return;
                    }

                    PublishIfCurrent(new PerformanceMetricsSnapshot(
                        generation,
                        cpuPercentage,
                        memory?.Percentage,
                        memory?.UsedGiB,
                        memory?.TotalGiB,
                        DateTimeOffset.UtcNow,
                        network?.DownloadMegabytesPerSecond,
                        network?.UploadMegabytesPerSecond),
                        cancellation,
                        token);
                }
                finally
                {
                    _samplingGate.Release();
                }

                await Task.Delay(_fastRefreshMilliseconds, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeGeneration, cancellation))
                {
                    _activeGeneration = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrentGeneration(
        long generation,
        CancellationTokenSource cancellation,
        CancellationToken token)
    {
        lock (_sync)
        {
            return !token.IsCancellationRequested &&
                   generation == _generation &&
                   ReferenceEquals(_activeGeneration, cancellation);
        }
    }

    private void PublishIfCurrent(
        PerformanceMetricsSnapshot snapshot,
        CancellationTokenSource cancellation,
        CancellationToken token)
    {
        lock (_sync)
        {
            if (token.IsCancellationRequested ||
                snapshot.Generation != _generation ||
                !ReferenceEquals(_activeGeneration, cancellation))
            {
                return;
            }

            _publish(snapshot);
        }
    }

    private CpuTimeCounters? ReadCpuTimesSafely()
    {
        try
        {
            return _source.ReadCpuTimes();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private MemoryReading? ReadMemorySafely()
    {
        try
        {
            var reading = _source.ReadPhysicalMemory();
            if (reading is null || reading.Value.TotalPhysicalBytes == 0 ||
                reading.Value.AvailablePhysicalBytes > reading.Value.TotalPhysicalBytes)
            {
                return null;
            }

            var total = reading.Value.TotalPhysicalBytes;
            var used = total - reading.Value.AvailablePhysicalBytes;
            var percentage = (int)decimal.Round(
                (decimal)used * 100m / total,
                decimals: 0,
                MidpointRounding.AwayFromZero);

            return new MemoryReading(
                Math.Clamp(percentage, 0, 100),
                (double)used / BytesPerGiB,
                (double)total / BytesPerGiB);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private NetworkCountersSnapshot? ReadNetworkCountersSafely()
    {
        try
        {
            return _source.ReadNetworkCounters();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private readonly record struct MemoryReading(int Percentage, double UsedGiB, double TotalGiB);

    private sealed class NetworkThroughputTracker
    {
        private const uint UnspecifiedPhysicalMedium = 0;
        private const double BytesPerMebibyte = 1024d * 1024;
        private readonly Dictionary<ulong, NetworkBaseline> _baselines = [];
        private readonly List<ulong> _staleBaselineLuids = [];
        private long _snapshotGeneration;

        public NetworkSpeedReading? Read(NetworkCountersSnapshot? snapshot)
        {
            if (snapshot is null || snapshot.Interfaces is null)
            {
                return null;
            }

            var hasBaseCandidate = false;
            var hasPhysicalCandidate = false;
            var fastestBaseCandidate = default(NetworkSpeedCandidate);
            var fastestPhysicalCandidate = default(NetworkSpeedCandidate);
            var snapshotGeneration = unchecked(++_snapshotGeneration);

            foreach (var row in snapshot.Interfaces)
            {
                if (!row.IsUp || row.IsLoopback)
                {
                    continue;
                }

                var speed = CalculateSpeed(row, snapshot.MonotonicTimestamp);
                if (!hasBaseCandidate ||
                    speed.TotalMegabytesPerSecond > fastestBaseCandidate.TotalMegabytesPerSecond)
                {
                    fastestBaseCandidate = speed;
                }

                hasBaseCandidate = true;
                if (row.PhysicalMediumType != UnspecifiedPhysicalMedium)
                {
                    if (!hasPhysicalCandidate ||
                        speed.TotalMegabytesPerSecond > fastestPhysicalCandidate.TotalMegabytesPerSecond)
                    {
                        fastestPhysicalCandidate = speed;
                    }

                    hasPhysicalCandidate = true;
                }

                // 物理/回退筛选只影响本轮胜出者，所有基础候选都保留最新差分基线。
                _baselines[row.InterfaceLuid] = new NetworkBaseline(
                    row,
                    snapshot.MonotonicTimestamp,
                    snapshotGeneration);
            }

            // 成功读表后，基线只保留仍处于 Up 且非 loopback 的接口；物理筛选变化不影响基线。
            _staleBaselineLuids.Clear();
            foreach (var baseline in _baselines)
            {
                if (baseline.Value.SnapshotGeneration != snapshotGeneration)
                {
                    _staleBaselineLuids.Add(baseline.Key);
                }
            }

            foreach (var luid in _staleBaselineLuids)
            {
                _baselines.Remove(luid);
            }

            _staleBaselineLuids.Clear();

            if (!hasBaseCandidate)
            {
                return new NetworkSpeedReading(0, 0);
            }

            var fastest = hasPhysicalCandidate ? fastestPhysicalCandidate : fastestBaseCandidate;
            return new NetworkSpeedReading(
                RoundMegabytesPerSecond(fastest.DownloadMegabytesPerSecond),
                RoundMegabytesPerSecond(fastest.UploadMegabytesPerSecond));
        }

        private NetworkSpeedCandidate CalculateSpeed(
            NetworkInterfaceCounters current,
            TimeSpan monotonicTimestamp)
        {
            if (!_baselines.TryGetValue(current.InterfaceLuid, out var previous))
            {
                return new NetworkSpeedCandidate(0, 0);
            }

            var elapsedSeconds = ((double)monotonicTimestamp.Ticks - previous.MonotonicTimestamp.Ticks) /
                                 TimeSpan.TicksPerSecond;
            if (elapsedSeconds <= 0)
            {
                return new NetworkSpeedCandidate(0, 0);
            }

            var download = CalculateDirectionSpeed(current.InOctets, previous.Counters.InOctets, elapsedSeconds);
            var upload = CalculateDirectionSpeed(current.OutOctets, previous.Counters.OutOctets, elapsedSeconds);
            return new NetworkSpeedCandidate(download, upload);
        }

        private static double CalculateDirectionSpeed(ulong current, ulong previous, double elapsedSeconds)
        {
            if (current < previous)
            {
                return 0;
            }

            return (current - previous) / elapsedSeconds / BytesPerMebibyte;
        }

        private static double RoundMegabytesPerSecond(double value) =>
            Math.Round(value, 1, MidpointRounding.AwayFromZero);

        private readonly record struct NetworkBaseline(
            NetworkInterfaceCounters Counters,
            TimeSpan MonotonicTimestamp,
            long SnapshotGeneration);

        private readonly record struct NetworkSpeedCandidate(
            double DownloadMegabytesPerSecond,
            double UploadMegabytesPerSecond)
        {
            public double TotalMegabytesPerSecond => DownloadMegabytesPerSecond + UploadMegabytesPerSecond;
        }
    }

    private readonly record struct NetworkSpeedReading(
        double DownloadMegabytesPerSecond,
        double UploadMegabytesPerSecond);

    private sealed class CpuUsageTracker
    {
        private CpuTimeCounters? _previous;

        public int? ReadPercentage(CpuTimeCounters? current)
        {
            if (current is null)
            {
                return null;
            }

            var value = current.Value;
            if (_previous is not { } previous)
            {
                _previous = value;
                return 0;
            }

            _previous = value;
            if (value.KernelTime < previous.KernelTime ||
                value.UserTime < previous.UserTime ||
                value.IdleTime < previous.IdleTime)
            {
                return null;
            }

            var kernelDelta = (UInt128)(value.KernelTime - previous.KernelTime);
            var userDelta = (UInt128)(value.UserTime - previous.UserTime);
            var idleDelta = (UInt128)(value.IdleTime - previous.IdleTime);
            var totalDelta = kernelDelta + userDelta;
            if (totalDelta == 0)
            {
                return 0;
            }

            if (idleDelta > totalDelta)
            {
                return null;
            }

            var busyDelta = totalDelta - idleDelta;
            var percentage = (int)decimal.Round(
                (decimal)busyDelta * 100m / (decimal)totalDelta,
                decimals: 0,
                MidpointRounding.AwayFromZero);
            return Math.Clamp(percentage, 0, 100);
        }
    }
}
