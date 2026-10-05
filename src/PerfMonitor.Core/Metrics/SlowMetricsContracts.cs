namespace PerfMonitor.Core.Metrics;

public sealed record GpuMetricsReading(
    int UtilizationPercentage,
    int MemoryUtilizationPercentage,
    int TemperatureCelsius);

public sealed record SlowMetricsReading(
    GpuMetricsReading? Gpu,
    int? CpuTemperatureCelsius);

public interface ISlowMetricsSource
{
    Task<GpuMetricsReading?> ReadGpuMetricsAsync(CancellationToken cancellationToken);

    Task<int?> ReadCpuTemperatureCelsiusAsync(CancellationToken cancellationToken);
}
