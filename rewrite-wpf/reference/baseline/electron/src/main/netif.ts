import koffi from 'koffi';

// 网卡收发字节计数直接走 iphlpapi 的 GetIfTable2Ex，不再让 systeminformation
// 每轮采样起一个 powershell.exe（峰值私有 110MB，占空比约 1/3）。
const IS_WINDOWS = process.platform === 'win32';

const NO_ERROR = 0;
const MIB_IF_ENTRY_NORMAL = 0;

// ifdef.h: IF_OPER_STATUS 用的是 RFC 2863 取值，up 是 1。
export const IF_OPER_STATUS_UP = 1;
// ipficons.h: 回环适配器不参与网速，和 Get-NetAdapterStatistics 的口径一致。
export const IF_TYPE_SOFTWARE_LOOPBACK = 24;
// netoidis.h: NDIS_PHYSICAL_MEDIUM，0 = Unspecified。虚拟网卡（TUN、Hyper-V 虚拟交换机、
// 容器网卡）一律留 0，这正是 `Get-NetAdapter -Physical` 的筛选口径。
export const NDIS_PHYSICAL_MEDIUM_UNSPECIFIED = 0;

export interface NetIfRow {
  index: number;
  type: number;
  up: boolean;
  /** 有真实物理介质（等价于 Get-NetAdapter -Physical），虚拟网卡为 false。 */
  physical: boolean;
  inOctets: bigint;
  outOctets: bigint;
}

// MIB_IF_ROW2（netioapi.h）。这里只借 koffi 的布局计算，字段一个都不整块解码：
// 整块 decode 会把两个 WCHAR[257] 拷成 JS 数组，而按地址解 UTF-16 字符串在
// Electron 下会直接 AV，所以只按 offsetof 取需要的标量。
const MIB_IF_ROW2 = koffi.struct('MIB_IF_ROW2', {
  InterfaceLuid: 'uint64',
  InterfaceIndex: 'uint32',
  InterfaceGuid: koffi.array('uint8', 16),
  Alias: koffi.array('uint16', 257),
  Description: koffi.array('uint16', 257),
  PhysicalAddressLength: 'uint32',
  PhysicalAddress: koffi.array('uint8', 32),
  PermanentPhysicalAddress: koffi.array('uint8', 32),
  Mtu: 'uint32',
  Type: 'uint32',
  TunnelType: 'uint32',
  MediaType: 'uint32',
  PhysicalMediumType: 'uint32',
  AccessType: 'uint32',
  DirectionType: 'uint32',
  InterfaceAndOperStatusFlags: 'uint8', // 8 个 BOOLEAN:1 位域，共 1 字节
  OperStatus: 'uint32',
  AdminStatus: 'uint32',
  MediaConnectState: 'uint32',
  NetworkGuid: koffi.array('uint8', 16),
  ConnectionType: 'uint32',
  TransmitLinkSpeed: 'uint64',
  ReceiveLinkSpeed: 'uint64',
  InOctets: 'uint64',
  InUcastPkts: 'uint64',
  InNUcastPkts: 'uint64',
  InDiscards: 'uint64',
  InErrors: 'uint64',
  InUnknownProtos: 'uint64',
  InUcastOctets: 'uint64',
  InMulticastOctets: 'uint64',
  InBroadcastOctets: 'uint64',
  OutOctets: 'uint64',
  OutUcastPkts: 'uint64',
  OutNUcastPkts: 'uint64',
  OutDiscards: 'uint64',
  OutErrors: 'uint64',
  OutUcastOctets: 'uint64',
  OutMulticastOctets: 'uint64',
  OutBroadcastOctets: 'uint64',
  OutQLen: 'uint64'
});

// MIB_IF_TABLE2 的表头；Table 是变长数组，这里只声明 1 行来拿正确的行起始偏移。
const MIB_IF_TABLE2_HEAD = koffi.struct('MIB_IF_TABLE2_HEAD', {
  NumEntries: 'uint32',
  Table: koffi.array(MIB_IF_ROW2, 1)
});

const ROW_SIZE = koffi.sizeof(MIB_IF_ROW2);
const ROWS_OFFSET = koffi.offsetof(MIB_IF_TABLE2_HEAD, 'Table');
const OFF_INDEX = koffi.offsetof(MIB_IF_ROW2, 'InterfaceIndex');
const OFF_TYPE = koffi.offsetof(MIB_IF_ROW2, 'Type');
const OFF_OPER_STATUS = koffi.offsetof(MIB_IF_ROW2, 'OperStatus');
const OFF_PHYSICAL_MEDIUM = koffi.offsetof(MIB_IF_ROW2, 'PhysicalMediumType');
const OFF_IN_OCTETS = koffi.offsetof(MIB_IF_ROW2, 'InOctets');
const OFF_OUT_OCTETS = koffi.offsetof(MIB_IF_ROW2, 'OutOctets');

const iphlpapi = IS_WINDOWS ? koffi.load('iphlpapi.dll') : null;

// 注意用 GetIfTable2Ex：同名的 GetIfTable2 在本机调用时直接 access violation，
// 两者只差一个 MIB_IF_ENTRY_LEVEL 参数。
const getIfTable2Ex = iphlpapi?.func('__stdcall', 'GetIfTable2Ex', 'uint32', [
  'uint32',
  '_Out_ void **'
]);
const freeMibTable = iphlpapi?.func('__stdcall', 'FreeMibTable', 'void', ['void*']);

function asAddress(value: unknown): bigint | null {
  if (typeof value === 'bigint') return value === 0n ? null : value;
  if (typeof value === 'number') return value === 0 ? null : BigInt(value);
  return null;
}

function readUint32(base: bigint, offset: number): number {
  return Number(koffi.decode(base + BigInt(offset), 'uint32'));
}

function readUint64(base: bigint, offset: number): bigint {
  return asAddress(koffi.decode(base + BigInt(offset), 'uint64')) ?? 0n;
}

/** 读取全部网卡计数器；非 Windows 或调用失败时返回空数组。 */
export function readInterfaceRows(): NetIfRow[] {
  const rows: NetIfRow[] = [];
  if (!getIfTable2Ex) return rows;

  const slot = koffi.alloc('void*', 1);
  try {
    if (getIfTable2Ex(MIB_IF_ENTRY_NORMAL, slot) !== NO_ERROR) return rows;
    const table = asAddress(koffi.decode(slot, 'void*'));
    if (!table) return rows;

    try {
      const count = readUint32(table, 0);
      for (let i = 0; i < count; i++) {
        const row = table + BigInt(ROWS_OFFSET + i * ROW_SIZE);
        rows.push({
          index: readUint32(row, OFF_INDEX),
          type: readUint32(row, OFF_TYPE),
          up: readUint32(row, OFF_OPER_STATUS) === IF_OPER_STATUS_UP,
          physical: readUint32(row, OFF_PHYSICAL_MEDIUM) !== NDIS_PHYSICAL_MEDIUM_UNSPECIFIED,
          inOctets: readUint64(row, OFF_IN_OCTETS),
          outOctets: readUint64(row, OFF_OUT_OCTETS)
        });
      }
    } finally {
      freeMibTable?.(koffi.as(table, 'void*'));
    }
  } catch {
    return rows;
  } finally {
    koffi.free(slot);
  }

  return rows;
}
