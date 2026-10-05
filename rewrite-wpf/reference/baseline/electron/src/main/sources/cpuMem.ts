import { cpus, freemem, totalmem } from 'node:os';
import type { CpuMemSample } from './types';

// CPU 使用率：os.cpus() 的累计时间片差分，口径是全机合计。
// 首次读取没有差分窗口，返回 0（不是 null），属正常语义。
let lastIdle = 0;
let lastTotal = 0;
let initialized = false;

function cpuPct(): number {
  let idle = 0;
  let total = 0;
  for (const core of cpus()) {
    idle += core.times.idle;
    total += core.times.user + core.times.nice + core.times.sys + core.times.idle + core.times.irq;
  }
  if (!initialized) {
    lastIdle = idle;
    lastTotal = total;
    initialized = true;
    return 0;
  }
  const idleDelta = idle - lastIdle;
  const totalDelta = total - lastTotal;
  lastIdle = idle;
  lastTotal = total;
  if (totalDelta <= 0) return 0;
  return Math.min(100, Math.max(0, Math.round(100 * (1 - idleDelta / totalDelta))));
}

export function readCpuMem(): CpuMemSample {
  const totalGb = Math.round(totalmem() / 1024 ** 3);
  const freeGb = freemem() / 1024 ** 3;
  const usedGb = Math.round(totalGb - freeGb);
  const memPct = totalGb > 0 ? Math.round((usedGb / totalGb) * 100) : 0;
  return { cpuPct: cpuPct(), memUsedGb: usedGb, memTotalGb: totalGb, memPct };
}
