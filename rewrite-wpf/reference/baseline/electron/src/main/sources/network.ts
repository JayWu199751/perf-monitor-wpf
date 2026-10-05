import { IF_TYPE_SOFTWARE_LOOPBACK, readInterfaceRows } from '../netif';
import type { NetworkSample } from './types';

// 网卡字节计数改走进程内 iphlpapi，不再经 systeminformation 起 PowerShell。
//
// 差分状态刻意放在可注入时间的 tracker 里：读表那一步只有 Windows 跑得起来，
// 而"两个累计读数怎么算成速率"是纯算术，拆开后任何平台都能测。
export interface NetRow {
  index: number;
  physical: boolean;
  inOctets: bigint;
  outOctets: bigint;
}

// 只有物理网卡的计数等于真实上线流量，虚拟网卡会把同一份流量重复计入。本机实测：
// mihomo 的 Meta Tunnel（TUN，PhysicalMediumType = Unspecified）把 15.4 MB/s 的下载
// 记成 47.4 MB/s，约 3 倍，而下面"取收发之和最大"的选法正好会挑中它。
// 所以候选集先收窄到物理网卡，口径与 `Get-NetAdapter -Physical` 一致。
// 收窄之后仍然取最大而不是求和：同一块物理网卡的 filter 子接口共享同一份计数，
// 本机 WLAN 就有 6 行同 MAC、同计数的记录，求和会翻 6 倍。
// 整机一块物理网卡都没有时（驱动没填 PhysicalMediumType）退回全集，
// 宁可读数偏大也不要永远显示 0。
export function selectNetRows(rows: NetRow[]): NetRow[] {
  const physical = rows.filter((r) => r.physical);
  return physical.length > 0 ? physical : rows;
}

// 64 位字节计数器要回绕得先跑满 2^64 字节，实际不可能；读数变小只可能是驱动重置了
// 网卡统计（例如适配器重启），这一轮按 0 处理，不能报出一个天文数字。
function octetDelta(current: bigint, before: bigint): bigint {
  return current >= before ? current - before : 0n;
}

export function createNetRateTracker() {
  let previous: Map<number, NetRow> | null = null;
  let previousAt = 0;

  return {
    // 取"收发速率之和最大的那块物理网卡"：多张物理网卡（有线 + 无线同时插着）时以主用为准。
    // 是取最大而不是求和，因为同一块物理网卡的 filter 子接口共享同一份字节计数（ADR-0004）。
    sample(rows: NetRow[], now: number): NetworkSample {
      const candidates = selectNetRows(rows);
      const current = new Map<number, NetRow>();
      // 基线存全量：某一轮选法变了（网卡插拔、介质字段翻转）也不会丢掉没被选中那块的基线。
      for (const row of rows) current.set(row.index, row);
      const elapsedMs = previous === null ? 0 : now - previousAt;
      let best: { down: number; up: number } | null = null;

      if (previous !== null && elapsedMs > 0) {
        const seconds = elapsedMs / 1000;
        for (const row of candidates) {
          const before = previous.get(row.index);
          if (!before) continue;
          const down = Number(octetDelta(row.inOctets, before.inOctets)) / seconds;
          const up = Number(octetDelta(row.outOctets, before.outOctets)) / seconds;
          if (!best || down + up > best.down + best.up) best = { down, up };
        }
      }

      previous = current;
      previousAt = now;
      if (!best) return { downMBs: 0, upMBs: 0 };
      const mb = (v: number): number => Math.round((v / 1024 / 1024) * 10) / 10;
      return { downMBs: mb(best.down), upMBs: mb(best.up) };
    },

    reset(): void {
      previous = null;
      previousAt = 0;
    }
  };
}

const tracker = createNetRateTracker();

function readRows(): NetRow[] {
  return readInterfaceRows()
    .filter((row) => row.up && row.type !== IF_TYPE_SOFTWARE_LOOPBACK)
    .map((row) => ({
      index: row.index,
      physical: row.physical,
      inOctets: row.inOctets,
      outOctets: row.outOctets
    }));
}

// 首轮没有差分窗口，只登记基线；启动时预热一次，避免第一轮全 0。
export function warmNetwork(): void {
  try {
    tracker.sample(readRows(), Date.now());
  } catch {
    // ignore
  }
}

export function readNetwork(): NetworkSample {
  return tracker.sample(readRows(), Date.now());
}
