namespace PerfMonitor.Core.Metrics;

public readonly record struct CpuTimeCounters(ulong KernelTime, ulong UserTime, ulong IdleTime);

public readonly record struct PhysicalMemoryCounters(ulong TotalPhysicalBytes, ulong AvailablePhysicalBytes);

public sealed record PerformanceMetricsSnapshot(
    long Generation,
    int? CpuPercentage,
    int? MemoryPercentage,
    double? MemoryUsedGiB,
    double? MemoryTotalGiB,
    DateTimeOffset Timestamp);

public interface ISystemMetricsSource
{
    CpuTimeCounters? ReadCpuTimes();

    PhysicalMemoryCounters? ReadPhysicalMemory();
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
                    PublishIfCurrent(new PerformanceMetricsSnapshot(
                        generation,
                        cpuPercentage,
                        memory?.Percentage,
                        memory?.UsedGiB,
                        memory?.TotalGiB,
                        DateTimeOffset.UtcNow),
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

    private readonly record struct MemoryReading(int Percentage, double UsedGiB, double TotalGiB);

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
