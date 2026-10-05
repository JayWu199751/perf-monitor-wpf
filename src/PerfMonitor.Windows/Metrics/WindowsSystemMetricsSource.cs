using System.Runtime.InteropServices;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Windows.Metrics;

public sealed class WindowsSystemMetricsSource : ISystemMetricsSource
{
    public CpuTimeCounters? ReadCpuTimes()
    {
        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return null;
        }

        return new CpuTimeCounters(
            ToUInt64(kernelTime),
            ToUInt64(userTime),
            ToUInt64(idleTime));
    }

    public PhysicalMemoryCounters? ReadPhysicalMemory()
    {
        var status = new MemoryStatusEx
        {
            Length = checked((uint)Marshal.SizeOf<MemoryStatusEx>())
        };

        return NativeMethods.GlobalMemoryStatusEx(ref status)
            ? new PhysicalMemoryCounters(status.TotalPhysical, status.AvailablePhysical)
            : null;
    }

    private static ulong ToUInt64(NativeFileTime value) =>
        ((ulong)value.HighDateTime << 32) | value.LowDateTime;

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(
            out NativeFileTime idleTime,
            out NativeFileTime kernelTime,
            out NativeFileTime userTime);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint LowDateTime;

        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;

        public uint MemoryLoad;

        public ulong TotalPhysical;

        public ulong AvailablePhysical;

        public ulong TotalPageFile;

        public ulong AvailablePageFile;

        public ulong TotalVirtual;

        public ulong AvailableVirtual;

        public ulong AvailableExtendedVirtual;
    }
}
