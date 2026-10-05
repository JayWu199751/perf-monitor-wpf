using PerfMonitor.Windows.Metrics;
using Xunit.Abstractions;

namespace PerfMonitor.Windows.Tests;

public sealed class WindowsSlowMetricsSourceTests
{
    private readonly ITestOutputHelper _output;

    public WindowsSlowMetricsSourceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(DisplayName = "Windows 慢指标 adapter 可查询本机 GPU 与 ACPI 数据或安全返回缺失")]
    public async Task Reads_optional_gpu_and_acpi_metrics_without_leaking_query_resources()
    {
        var source = new WindowsSlowMetricsSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));

        var gpu = await source.ReadGpuMetricsAsync(timeout.Token);
        var temperature = await source.ReadCpuTemperatureCelsiusAsync(timeout.Token);

        if (gpu is null)
        {
            _output.WriteLine("本机没有可读的 NVIDIA nvidia-smi 有效记录；GPU 指标按缺失处理。");
        }
        else
        {
            Assert.InRange(gpu.UtilizationPercentage, 0, 100);
            Assert.InRange(gpu.MemoryUtilizationPercentage, 0, 100);
            Assert.True(gpu.TemperatureCelsius >= 0);
            _output.WriteLine(
                $"本机 GPU 读数: 使用率={gpu.UtilizationPercentage}%，显存={gpu.MemoryUtilizationPercentage}%，温度={gpu.TemperatureCelsius}°。");
        }

        if (temperature is null)
        {
            _output.WriteLine("本机 ACPI 热区没有可读温度；按缺失处理。");
        }
        else
        {
            Assert.InRange(temperature.Value, 1, 119);
            _output.WriteLine($"本机最大有效 ACPI 热区温度: {temperature.Value}°。");
        }
    }

    [Fact(DisplayName = "慢指标 adapter 在调用前取消时不会启动系统查询")]
    public async Task Honors_cancellation_before_starting_system_queries()
    {
        var source = new WindowsSlowMetricsSource();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.ReadGpuMetricsAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.ReadCpuTemperatureCelsiusAsync(cancellation.Token));
    }
}
