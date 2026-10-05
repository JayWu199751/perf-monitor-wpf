using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using PerfMonitor.Core.Metrics;

namespace PerfMonitor.Windows.Metrics;

public sealed class WindowsSystemMetricsSource : ISystemMetricsSource
{
    private const int RelationGroup = 4;
    private const int ErrorInsufficientBuffer = 122;
    private const int SystemLogicalProcessorInformationHeaderSize = 8;
    private const int GroupRelationshipHeaderSize = 8 + 2 + 2 + 20;
    private const int ProcessorGroupInfoActiveProcessorCountOffset = 1;
    private const int ProcessorGroupInfoActiveProcessorMaskOffset = 40;

    public CpuTimeCounters? ReadCpuTimes()
    {
        if (!TryGetActiveProcessorGroups(out var groups))
        {
            return null;
        }

        ulong kernelTotal = 0;
        ulong userTotal = 0;
        ulong idleTotal = 0;
        var allGroupsRead = true;
        var hasOriginalAffinity = false;
        var originalAffinity = default(GroupAffinity);
        var currentThread = NativeMethods.GetCurrentThread();

        try
        {
            foreach (var group in groups)
            {
                var processorMask = group.ActiveProcessorMask & unchecked(0UL - group.ActiveProcessorMask);
                if (!TryToNativeMask(processorMask, out var nativeMask))
                {
                    allGroupsRead = false;
                    break;
                }

                var requestedAffinity = new GroupAffinity
                {
                    Mask = nativeMask,
                    Group = group.Group
                };

                if (!NativeMethods.SetThreadGroupAffinity(
                    currentThread,
                    ref requestedAffinity,
                    out var previousAffinity))
                {
                    allGroupsRead = false;
                    break;
                }

                if (!hasOriginalAffinity)
                {
                    originalAffinity = previousAffinity;
                    hasOriginalAffinity = true;
                }

                if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
                {
                    allGroupsRead = false;
                    break;
                }

                try
                {
                    kernelTotal = checked(kernelTotal + ToUInt64(kernelTime));
                    userTotal = checked(userTotal + ToUInt64(userTime));
                    idleTotal = checked(idleTotal + ToUInt64(idleTime));
                }
                catch (OverflowException)
                {
                    allGroupsRead = false;
                    break;
                }
            }

            if (allGroupsRead &&
                (!TryGetActiveProcessorGroups(out var groupsAfterRead) || !groups.SequenceEqual(groupsAfterRead)))
            {
                allGroupsRead = false;
            }
        }
        finally
        {
            if (hasOriginalAffinity &&
                !NativeMethods.SetThreadGroupAffinity(currentThread, ref originalAffinity, out _))
            {
                allGroupsRead = false;
            }
        }

        return allGroupsRead
            ? new CpuTimeCounters(kernelTotal, userTotal, idleTotal, checked((ushort)groups.Length))
            : null;
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

    private static bool TryToNativeMask(ulong value, out UIntPtr nativeMask)
    {
        if (UIntPtr.Size == sizeof(uint))
        {
            if (value > uint.MaxValue)
            {
                nativeMask = UIntPtr.Zero;
                return false;
            }

            nativeMask = new UIntPtr((uint)value);
            return true;
        }

        nativeMask = new UIntPtr(value);
        return true;
    }

    private static ulong FromNativeMask(IntPtr buffer, int offset) =>
        UIntPtr.Size == sizeof(uint)
            ? unchecked((uint)Marshal.ReadInt32(buffer, offset))
            : unchecked((ulong)Marshal.ReadInt64(buffer, offset));

    private static bool TryGetActiveProcessorGroups(out ProcessorGroup[] groups)
    {
        groups = [];
        var activeGroupCount = NativeMethods.GetActiveProcessorGroupCount();
        if (activeGroupCount == 0)
        {
            return false;
        }

        uint requiredLength = 0;
        if (NativeMethods.GetLogicalProcessorInformationEx(RelationGroup, IntPtr.Zero, ref requiredLength) ||
            Marshal.GetLastWin32Error() != ErrorInsufficientBuffer ||
            requiredLength < GroupRelationshipHeaderSize ||
            requiredLength > int.MaxValue)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal((int)requiredLength);
        try
        {
            var returnedLength = requiredLength;
            if (!NativeMethods.GetLogicalProcessorInformationEx(RelationGroup, buffer, ref returnedLength) ||
                returnedLength > requiredLength ||
                returnedLength > int.MaxValue ||
                !TryParseProcessorGroups(buffer, (int)returnedLength, out var parsedGroups) ||
                parsedGroups.Length != activeGroupCount)
            {
                return false;
            }

            for (var index = 0; index < parsedGroups.Length; index++)
            {
                var group = parsedGroups[index];
                if (group.Group != index ||
                    NativeMethods.GetActiveProcessorCount(group.Group) != group.ActiveProcessorCount)
                {
                    return false;
                }
            }

            groups = parsedGroups;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryParseProcessorGroups(
        IntPtr buffer,
        int bufferLength,
        out ProcessorGroup[] groups)
    {
        groups = [];
        List<ProcessorGroup>? parsedGroups = null;
        var offset = 0;
        var processorGroupInfoSize = ProcessorGroupInfoActiveProcessorMaskOffset + IntPtr.Size;

        while (offset < bufferLength)
        {
            if (bufferLength - offset < SystemLogicalProcessorInformationHeaderSize)
            {
                return false;
            }

            var relationship = Marshal.ReadInt32(buffer, offset);
            var structureSize = Marshal.ReadInt32(buffer, offset + sizeof(int));
            if (structureSize < SystemLogicalProcessorInformationHeaderSize ||
                structureSize > bufferLength - offset)
            {
                return false;
            }

            if (relationship == RelationGroup)
            {
                if (parsedGroups is not null || structureSize < GroupRelationshipHeaderSize)
                {
                    return false;
                }

                var activeGroupCount = unchecked((ushort)Marshal.ReadInt16(
                    buffer,
                    offset + SystemLogicalProcessorInformationHeaderSize + sizeof(ushort)));
                var groupInfoOffset = offset + GroupRelationshipHeaderSize;
                var requiredStructureSize = (long)GroupRelationshipHeaderSize +
                                            (long)activeGroupCount * processorGroupInfoSize;
                if (activeGroupCount == 0 || requiredStructureSize > structureSize)
                {
                    return false;
                }

                parsedGroups = new List<ProcessorGroup>(activeGroupCount);
                for (var index = 0; index < activeGroupCount; index++)
                {
                    var groupInfo = groupInfoOffset + index * processorGroupInfoSize;
                    var activeProcessorCount = Marshal.ReadByte(
                        buffer,
                        groupInfo + ProcessorGroupInfoActiveProcessorCountOffset);
                    var activeProcessorMask = FromNativeMask(
                        buffer,
                        groupInfo + ProcessorGroupInfoActiveProcessorMaskOffset);
                    if (activeProcessorCount == 0 ||
                        activeProcessorMask == 0 ||
                        BitOperations.PopCount(activeProcessorMask) != activeProcessorCount)
                    {
                        return false;
                    }

                    parsedGroups.Add(new ProcessorGroup((ushort)index, activeProcessorCount, activeProcessorMask));
                }
            }

            offset += structureSize;
        }

        if (parsedGroups is null)
        {
            return false;
        }

        groups = parsedGroups.ToArray();
        return true;
    }

    private readonly record struct ProcessorGroup(ushort Group, byte ActiveProcessorCount, ulong ActiveProcessorMask);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern ushort GetActiveProcessorGroupCount();

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern uint GetActiveProcessorCount(ushort groupNumber);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetLogicalProcessorInformationEx(
            int relationshipType,
            IntPtr buffer,
            ref uint returnedLength);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetThreadGroupAffinity(
            IntPtr thread,
            ref GroupAffinity groupAffinity,
            out GroupAffinity previousGroupAffinity);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct GroupAffinity
    {
        public UIntPtr Mask;

        public ushort Group;

        public ushort Reserved0;

        public ushort Reserved1;

        public ushort Reserved2;
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
