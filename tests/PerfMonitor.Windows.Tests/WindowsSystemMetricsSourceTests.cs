using System.Runtime.InteropServices;
using PerfMonitor.Windows.Metrics;
using Xunit.Abstractions;

namespace PerfMonitor.Windows.Tests;

public sealed class WindowsSystemMetricsSourceTests(ITestOutputHelper output)
{
    [Fact(DisplayName = "CPU 读数覆盖本机活动处理器组并恢复调用线程亲和性")]
    public void Reads_cpu_times_for_active_groups_and_restores_thread_affinity()
    {
        var expectedGroupCount = NativeMethods.GetActiveProcessorGroupCount();
        Assert.True(expectedGroupCount > 0);
        Assert.True(NativeMethods.GetThreadGroupAffinity(
            NativeMethods.GetCurrentThread(),
            out var affinityBefore));

        var cpu = new WindowsSystemMetricsSource().ReadCpuTimes();

        Assert.True(NativeMethods.GetThreadGroupAffinity(
            NativeMethods.GetCurrentThread(),
            out var affinityAfter));
        Assert.NotNull(cpu);
        Assert.Equal(expectedGroupCount, cpu.Value.ProcessorGroupCount);
        Assert.True(cpu.Value.KernelTime >= cpu.Value.IdleTime);
        Assert.Equal(affinityBefore.Group, affinityAfter.Group);
        Assert.Equal(affinityBefore.Mask, affinityAfter.Mask);

        output.WriteLine($"当前主机活动处理器组数: {expectedGroupCount}");
        output.WriteLine(
            $"聚合 API 读数: kernel={cpu.Value.KernelTime}, user={cpu.Value.UserTime}, idle={cpu.Value.IdleTime}");
        output.WriteLine(
            $"调用线程亲和性已恢复: group={affinityAfter.Group}, mask=0x{affinityAfter.Mask.ToUInt64():X}");
    }

    [Fact(DisplayName = "Windows 系统源可读取物理内存原始字节")]
    public void Reads_physical_memory()
    {
        var memory = new WindowsSystemMetricsSource().ReadPhysicalMemory();

        Assert.NotNull(memory);
        Assert.True(memory.Value.TotalPhysicalBytes > 0);
        Assert.True(memory.Value.AvailablePhysicalBytes <= memory.Value.TotalPhysicalBytes);
    }

    [Fact(DisplayName = "Windows 系统源可读取网卡计数和单调时间戳")]
    public void Reads_network_counters_and_monotonic_timestamp()
    {
        var network = new WindowsSystemMetricsSource().ReadNetworkCounters();

        Assert.NotNull(network);
        Assert.InRange(network.Interfaces.Count, 1, 4096);
        Assert.True(network.MonotonicTimestamp > TimeSpan.Zero);
        Assert.All(network.Interfaces, row => Assert.NotEqual(0UL, row.InterfaceLuid));
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern ushort GetActiveProcessorGroupCount();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetThreadGroupAffinity(
            IntPtr thread,
            out GroupAffinity groupAffinity);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GroupAffinity
    {
        public UIntPtr Mask;

        public ushort Group;

        public ushort Reserved0;

        public ushort Reserved1;

        public ushort Reserved2;
    }
}
