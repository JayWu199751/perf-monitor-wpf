import type { MetricsSnapshot } from '../shared/types';
import type { CpuMemSample, GpuSample, NetworkSample } from './sources/types';

export interface MetricsReaders {
  readCpuMem: () => CpuMemSample | Promise<CpuMemSample>;
  readNetwork: () => NetworkSample | Promise<NetworkSample>;
  readGpu: () => GpuSample | null | Promise<GpuSample | null>;
  readCpuTemp: () => number | null | Promise<number | null>;
}

export interface MetricsOptions extends MetricsReaders {
  onSnapshot: (s: MetricsSnapshot) => void;
  refreshFastMs?: number;
  refreshSlowMs?: number;
}

// 指标采样服务：把四个数据源合并成完整快照，按快/慢两频推送。
// 快频（CPU/内存/网络）与慢频（GPU/温度）各缓存最近一次结果，
// 因此任一次采样都输出全量字段，渲染层无需关心某个字段来自哪一轮。
export function createMetricsService(opts: MetricsOptions) {
  const fastMs = opts.refreshFastMs ?? 1000;
  const slowMs = opts.refreshSlowMs ?? 3000;
  let running = false;
  let paused = false;
  let fastTimer: ReturnType<typeof setInterval> | null = null;
  let slowTimer: ReturnType<typeof setInterval> | null = null;

  // null = 读取失败 → 界面显示 `--`
  let cpuMem: CpuMemSample | null = null;
  let network: NetworkSample | null = null;
  let gpu: GpuSample | null = null;
  let cpuTemp: number | null = null;
  let moving = false;
  // ADR-0002：原生拖动期间不投递，只渲染最新快照。稳定拖动中采样已暂停，
  // 这个单槽缓冲只吸收在途竞态——setMoving(true) 前已越过关卡的采样在网络
  // 读取（实测可停顿 ~216ms）返回后才 emit，暂存这条快照，恢复时投递。
  let pending: MetricsSnapshot | null = null;

  function safe<T>(fn: () => T | Promise<T>): Promise<T | null> {
    return Promise.resolve()
      .then(fn)
      .catch(() => null);
  }

  function emit(): void {
    if (!running) return;
    const snap: MetricsSnapshot = {
      cpuPct: cpuMem?.cpuPct ?? null,
      cpuTemp,
      memUsedGb: cpuMem?.memUsedGb ?? null,
      memTotalGb: cpuMem?.memTotalGb ?? 0,
      memPct: cpuMem?.memPct ?? null,
      gpuPct: gpu?.gpuPct ?? null,
      gpuMemPct: gpu?.gpuMemPct ?? null,
      gpuTemp: gpu?.gpuTemp ?? null,
      netDownMBs: network?.downMBs ?? null,
      netUpMBs: network?.upMBs ?? null,
      ts: Date.now()
    };
    if (moving) {
      pending = snap;
      return;
    }
    opts.onSnapshot(snap);
  }

  async function sampleFast(): Promise<void> {
    if (paused || moving) return;
    const [cm, net] = await Promise.all([safe(opts.readCpuMem), safe(opts.readNetwork)]);
    cpuMem = cm;
    network = net;
    emit();
  }

  async function sampleSlow(): Promise<void> {
    if (paused || moving) return;
    const [g, t] = await Promise.all([safe(opts.readGpu), safe(opts.readCpuTemp)]);
    gpu = g;
    cpuTemp = t;
    emit();
  }

  return {
    start(): void {
      running = true;
      void sampleFast();
      void sampleSlow();
      fastTimer = setInterval(() => void sampleFast(), fastMs);
      slowTimer = setInterval(() => void sampleSlow(), slowMs);
    },
    stop(): void {
      running = false;
      if (fastTimer) clearInterval(fastTimer);
      if (slowTimer) clearInterval(slowTimer);
      fastTimer = null;
      slowTimer = null;
    },
    // 小窗隐藏时暂停采样：常驻后台的工具不该在看不见时继续消耗
    setPaused(p: boolean): void {
      const was = paused;
      paused = p;
      if (was && !p && !moving) {
        void sampleFast();
        void sampleSlow();
      }
    },
    // 原生拖动期间暂停（ADR-0002）：松手后补采样并只投递最新一条
    setMoving(p: boolean): void {
      const was = moving;
      moving = p;
      if (!p) {
        const snap = pending;
        pending = null;
        if (snap) opts.onSnapshot(snap);
      }
      if (was && !p && !paused) {
        void sampleFast();
        void sampleSlow();
      }
    }
  };
}
