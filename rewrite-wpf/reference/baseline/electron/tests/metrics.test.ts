import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMetricsService, type MetricsOptions } from '../src/main/metrics';

function options(overrides: Partial<MetricsOptions> = {}): MetricsOptions {
  return {
    readCpuMem: vi.fn(() => ({ cpuPct: 10, memUsedGb: 8, memTotalGb: 32, memPct: 25 })),
    readNetwork: vi.fn(async () => ({ downMBs: 1.5, upMBs: 0.5 })),
    readGpu: vi.fn(async () => ({ gpuPct: 30, gpuMemPct: 40, gpuTemp: 55 })),
    readCpuTemp: vi.fn(async () => 60),
    onSnapshot: vi.fn(),
    ...overrides
  };
}

const lastSnapshot = (onSnapshot: ReturnType<typeof vi.fn>) => {
  const calls = onSnapshot.mock.calls;
  return calls[calls.length - 1]?.[0];
};

describe('metrics service', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('emits a complete snapshot on every fast tick by caching slow fields', async () => {
    const opts = options();
    const service = createMetricsService(opts);
    service.start();
    await vi.advanceTimersByTimeAsync(0);
    expect(lastSnapshot(vi.mocked(opts.onSnapshot))?.gpuPct).toBe(30);

    // 第二轮只跑快频采样，GPU/温度仍应带着上一轮的缓存值
    await vi.advanceTimersByTimeAsync(1000);
    const snap = lastSnapshot(vi.mocked(opts.onSnapshot));
    expect(snap?.cpuPct).toBe(10);
    expect(snap?.gpuTemp).toBe(55);
    service.stop();
  });

  it('degrades a failing source to null instead of dropping the snapshot', async () => {
    const opts = options({ readGpu: vi.fn(async () => { throw new Error('no nvidia-smi'); }) });
    const service = createMetricsService(opts);
    service.start();
    await vi.advanceTimersByTimeAsync(0);
    const snap = lastSnapshot(vi.mocked(opts.onSnapshot));
    expect(snap?.gpuPct).toBeNull();
    expect(snap?.cpuPct).toBe(10);
    service.stop();
  });

  it('stops sampling while the widget is hidden and catches up on show', async () => {
    const opts = options();
    const service = createMetricsService(opts);
    service.start();
    await vi.advanceTimersByTimeAsync(0);
    service.setPaused(true);
    const before = vi.mocked(opts.onSnapshot).mock.calls.length;
    await vi.advanceTimersByTimeAsync(5000);
    expect(vi.mocked(opts.onSnapshot).mock.calls.length).toBe(before);

    service.setPaused(false);
    await vi.advanceTimersByTimeAsync(0);
    expect(vi.mocked(opts.onSnapshot).mock.calls.length).toBeGreaterThan(before);
    service.stop();
  });

  it('holds the in-flight snapshot during a drag and delivers only the latest on release', async () => {
    // ADR-0002 的竞态窗口：采样已经越过 moving 关卡，但要等网络/GPU 读取返回才 emit，
    // 期间用户已经开始拖动。此时不得投递（会与原生移动循环抢主线程），只留最新一条。
    let resolveNet: (v: { downMBs: number; upMBs: number }) => void = () => {};
    let resolveGpu: (v: { gpuPct: number; gpuMemPct: number; gpuTemp: number }) => void = () => {};
    const opts = options({
      readNetwork: () => new Promise((r) => (resolveNet = r)),
      readGpu: () => new Promise((r) => (resolveGpu = r))
    });
    const service = createMetricsService(opts);
    service.start();
    // 先让两路采样真正走到 await，拿到 reader 的 resolve 句柄
    await vi.advanceTimersByTimeAsync(0);
    service.setMoving(true);

    resolveNet({ downMBs: 2, upMBs: 1 });
    resolveGpu({ gpuPct: 44, gpuMemPct: 55, gpuTemp: 66 });
    await vi.advanceTimersByTimeAsync(0);
    expect(opts.onSnapshot).not.toHaveBeenCalled();

    service.setMoving(false);
    expect(opts.onSnapshot).toHaveBeenCalledTimes(1);
    // 两条在途快照合并成一条，且是最后完成的那条（含 GPU 缓存）
    expect(vi.mocked(opts.onSnapshot).mock.calls[0]?.[0]).toMatchObject({
      gpuPct: 44,
      netDownMBs: 2
    });
    service.stop();
  });
});
