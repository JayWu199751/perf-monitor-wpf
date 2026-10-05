namespace PerfMonitor.Core.Metrics;

internal sealed class PerformanceSlowMetricsSampler : IDisposable
{
    internal const int DefaultRefreshMilliseconds = 3000;
    private static readonly int[] AllowedRefreshIntervals = [3000, 5000];

    private readonly object _sync = new();
    private readonly SemaphoreSlim _samplingGate = new(1, 1);
    private readonly ISlowMetricsSource _source;
    private readonly Action<long, SlowMetricsReading> _publish;
    private int _refreshMilliseconds;
    private CancellationTokenSource? _activeGeneration;
    private long _generation;
    private bool _disposed;

    public PerformanceSlowMetricsSampler(
        ISlowMetricsSource source,
        Action<long, SlowMetricsReading> publish,
        int refreshMilliseconds = DefaultRefreshMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(publish);

        if (!AllowedRefreshIntervals.Contains(refreshMilliseconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshMilliseconds),
                "慢通道刷新间隔只能是 3000 或 5000 毫秒。");
        }

        _source = source;
        _publish = publish;
        _refreshMilliseconds = refreshMilliseconds;
    }

    public void SetRefreshMilliseconds(int milliseconds)
    {
        if (!AllowedRefreshIntervals.Contains(milliseconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(milliseconds),
                "慢通道刷新间隔只能是 3000 或 5000 毫秒。");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _refreshMilliseconds = milliseconds;
        }
    }

    public void Start(long generation)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activeGeneration is not null && _generation == generation)
            {
                return;
            }

            previous = _activeGeneration;
            _generation = generation;
            current = new CancellationTokenSource();
            _activeGeneration = current;
        }

        CancelIfActive(previous);
        _ = Task.Run(() => SampleGenerationAsync(generation, current));
    }

    public void Stop(long generation)
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            _generation = generation;
            cancellation = _activeGeneration;
            _activeGeneration = null;
        }

        CancelIfActive(cancellation);
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
            cancellation = _activeGeneration;
            _activeGeneration = null;
        }

        CancelIfActive(cancellation);
    }

    private static void CancelIfActive(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task SampleGenerationAsync(long generation, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _samplingGate.WaitAsync(token).ConfigureAwait(false);
                SlowMetricsReading reading;
                try
                {
                    if (!IsCurrentGeneration(generation, cancellation, token))
                    {
                        return;
                    }

                    var gpuTask = ReadGpuSafelyAsync(token);
                    var temperatureTask = ReadCpuTemperatureSafelyAsync(token);
                    await Task.WhenAll(gpuTask, temperatureTask).ConfigureAwait(false);
                    reading = new SlowMetricsReading(await gpuTask.ConfigureAwait(false), await temperatureTask.ConfigureAwait(false));
                }
                finally
                {
                    _samplingGate.Release();
                }

                PublishIfCurrent(generation, reading, cancellation, token);
                int refreshMilliseconds;
                lock (_sync)
                {
                    refreshMilliseconds = _refreshMilliseconds;
                }

                await Task.Delay(refreshMilliseconds, token).ConfigureAwait(false);
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

    private bool IsCurrentGeneration(long generation, CancellationTokenSource cancellation, CancellationToken token)
    {
        lock (_sync)
        {
            return !token.IsCancellationRequested &&
                   generation == _generation &&
                   ReferenceEquals(_activeGeneration, cancellation);
        }
    }

    private void PublishIfCurrent(
        long generation,
        SlowMetricsReading reading,
        CancellationTokenSource cancellation,
        CancellationToken token)
    {
        lock (_sync)
        {
            if (token.IsCancellationRequested ||
                generation != _generation ||
                !ReferenceEquals(_activeGeneration, cancellation))
            {
                return;
            }

            _publish(generation, reading);
        }
    }

    private async Task<GpuMetricsReading?> ReadGpuSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _source.ReadGpuMetricsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<int?> ReadCpuTemperatureSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _source.ReadCpuTemperatureCelsiusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
