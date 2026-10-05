using System.Diagnostics;
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

    public NetworkCountersSnapshot? ReadNetworkCounters()
    {
        IntPtr table = IntPtr.Zero;
        try
        {
            // MibIfTableNormal 使用过滤栈顶端的统计值，并枚举逻辑与物理接口。
            if (NativeMethods.GetIfTable2Ex(MibIfTableNormal, out table) != 0 || table == IntPtr.Zero)
            {
                return null;
            }

            var timestamp = Stopwatch.GetTimestamp();
            var header = Marshal.PtrToStructure<MibIfTable2Header>(table);
            if (header.NumEntries > MaximumInterfaceCount)
            {
                return null;
            }

            var rows = new List<NetworkInterfaceCounters>((int)header.NumEntries);
            var firstRowOffset = Marshal.OffsetOf<MibIfTable2Header>(nameof(MibIfTable2Header.FirstRow)).ToInt64();
            var rowSize = Marshal.SizeOf<MibIfRow2>();
            for (var index = 0; index < header.NumEntries; index++)
            {
                var rowOffset = checked(firstRowOffset + (long)index * rowSize);
                var row = Marshal.PtrToStructure<MibIfRow2>(IntPtr.Add(table, checked((int)rowOffset)));
                rows.Add(new NetworkInterfaceCounters(
                    row.InterfaceLuid,
                    IsUp: row.OperStatus == IfOperStatusUp,
                    IsLoopback: row.Type == IfTypeSoftwareLoopback || row.AccessType == NetIfAccessLoopback,
                    row.PhysicalMediumType,
                    row.InOctets,
                    row.OutOctets));
            }

            return new NetworkCountersSnapshot(Stopwatch.GetElapsedTime(0, timestamp), rows);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or MarshalDirectiveException)
        {
            return null;
        }
        finally
        {
            if (table != IntPtr.Zero)
            {
                NativeMethods.FreeMibTable(table);
            }
        }
    }

    private static ulong ToUInt64(NativeFileTime value) =>
        ((ulong)value.HighDateTime << 32) | value.LowDateTime;

    private const uint MibIfTableNormal = 0;
    private const uint IfOperStatusUp = 1;
    private const uint IfTypeSoftwareLoopback = 24;
    private const uint NetIfAccessLoopback = 1;
    private const uint MaximumInterfaceCount = 4096;

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

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        internal static extern uint GetIfTable2Ex(uint level, out IntPtr table);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        internal static extern void FreeMibTable(IntPtr memory);
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

    [StructLayout(LayoutKind.Sequential)]
    private struct MibIfTable2Header
    {
        public uint NumEntries;

        // 原生 Table 是可变长数组；该指针位只用于计算经 ABI 对齐后的数组偏移。
        public IntPtr FirstRow;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MibIfRow2
    {
        public ulong InterfaceLuid;

        public uint InterfaceIndex;

        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        public string Alias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        public string Description;

        public uint PhysicalAddressLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] PhysicalAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] PermanentPhysicalAddress;

        public uint Mtu;

        public uint Type;

        public uint TunnelType;

        public uint MediaType;

        public uint PhysicalMediumType;

        public uint AccessType;

        public uint DirectionType;

        public byte InterfaceAndOperStatusFlags;

        public uint OperStatus;

        public uint AdminStatus;

        public uint MediaConnectState;

        public Guid NetworkGuid;

        public uint ConnectionType;

        public ulong TransmitLinkSpeed;

        public ulong ReceiveLinkSpeed;

        public ulong InOctets;

        public ulong InUcastPkts;

        public ulong InNUcastPkts;

        public ulong InDiscards;

        public ulong InErrors;

        public ulong InUnknownProtos;

        public ulong InUcastOctets;

        public ulong InMulticastOctets;

        public ulong InBroadcastOctets;

        public ulong OutOctets;

        public ulong OutUcastPkts;

        public ulong OutNUcastPkts;

        public ulong OutDiscards;

        public ulong OutErrors;

        public ulong OutUcastOctets;

        public ulong OutMulticastOctets;

        public ulong OutBroadcastOctets;

        public ulong OutQLen;
    }
}
