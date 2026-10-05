using PerfMonitor.Windows.Metrics;

namespace PerfMonitor.Windows.Tests;

public sealed class WindowsSystemMetricsSourceTests
{
    [Fact(DisplayName = "Windows 系统源可读取 GetSystemTimes 与物理内存原始字节")]
    public void Reads_system_cpu_times_and_physical_memory()
    {
        var source = new WindowsSystemMetricsSource();

        var cpu = source.ReadCpuTimes();
        var memory = source.ReadPhysicalMemory();
        var network = source.ReadNetworkCounters();

        Assert.NotNull(cpu);
        Assert.True(cpu.Value.KernelTime >= cpu.Value.IdleTime);
        Assert.NotNull(memory);
        Assert.True(memory.Value.TotalPhysicalBytes > 0);
        Assert.True(memory.Value.AvailablePhysicalBytes <= memory.Value.TotalPhysicalBytes);
        Assert.NotNull(network);
        Assert.InRange(network.Interfaces.Count, 1, 4096);
        Assert.True(network.MonotonicTimestamp > TimeSpan.Zero);
        Assert.All(network.Interfaces, row => Assert.NotEqual(0UL, row.InterfaceLuid));
    }
}
