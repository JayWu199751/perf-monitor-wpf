import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import type { GpuSample } from './types';

const execFileP = promisify(execFile);

// NVIDIA-only：AMD/Intel 拿不到利用率/显存/温度，返回 null → 界面显示 `--`。
// windowsHide 显式置真：GUI 应用 spawn 控制台程序时不分配控制台窗口，
// 否则每 3 秒闪一次黑窗。
export async function readGpu(): Promise<GpuSample | null> {
  try {
    const { stdout } = await execFileP(
      'nvidia-smi',
      [
        '--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu',
        '--format=csv,noheader,nounits'
      ],
      { timeout: 5000, windowsHide: true }
    );
    const [util, memUsed, memTotal, temp] = stdout
      .trim()
      .split(',')
      .map((s) => Number(s.trim()));
    if (
      !Number.isFinite(util) ||
      !Number.isFinite(memUsed) ||
      !Number.isFinite(memTotal) ||
      memTotal <= 0 ||
      !Number.isFinite(temp)
    ) {
      return null;
    }
    return {
      gpuPct: Math.round(util),
      gpuMemPct: Math.round((memUsed / memTotal) * 100),
      gpuTemp: Math.round(temp)
    };
  } catch {
    return null;
  }
}
