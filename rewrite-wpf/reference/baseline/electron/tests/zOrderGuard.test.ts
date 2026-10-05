import { describe, expect, it } from 'vitest';
import {
  Z_GUARD_INTERVAL_MS,
  Z_ROW_REFRESH_TICKS,
  createZOrderGuard,
  type TaskbarSource
} from '../src/main/zOrderGuard';

// 守卫的三条实测结论（ADR-0005 后续修正一）此前内联在 widgetWindow.ts 里、被 electron import
// 挡着，一条都测不到。fake source 把「哪一拍问了什么」数出来，于是这些数字第一次有了回归网。
// 句柄这里故意用字符串：守卫必须对它完全透明，真机传的是 bigint。

interface FakeSource extends TaskbarSource<string> {
  inRow: boolean;
  isCovered: boolean;
  alive: boolean;
  refreshes: number;
  covers: number;
  lastCoverTaskbar: string | null | undefined;
}

function createSource(): FakeSource {
  const source: FakeSource = {
    inRow: false,
    isCovered: false,
    alive: true,
    refreshes: 0,
    covers: 0,
    lastCoverTaskbar: undefined,
    active() {
      return source.alive;
    },
    refresh() {
      source.refreshes++;
      return { inTaskbarRow: source.inRow, taskbar: 'HWND-7' };
    },
    covered(taskbar) {
      source.covers++;
      source.lastCoverTaskbar = taskbar;
      return source.isCovered;
    }
  };
  return source;
}

function harness(opts?: { intervalMs?: number; refreshTicks?: number }) {
  const source = createSource();
  let reasserts = 0;
  let tick: (() => void) | null = null;
  let startedWith = 0;
  let starts = 0;
  let stops = 0;
  const guard = createZOrderGuard<string>({
    source,
    reassert: () => {
      reasserts++;
    },
    startTimer: (cb, ms) => {
      starts++;
      tick = cb;
      startedWith = ms;
      return () => {
        stops++;
        tick = null;
      };
    },
    intervalMs: opts?.intervalMs,
    refreshTicks: opts?.refreshTicks
  });
  const poll = (n = 1): void => {
    for (let i = 0; i < n; i++) tick?.();
  };
  return {
    source,
    guard,
    poll,
    get reasserts() {
      return reasserts;
    },
    get startedWith() {
      return startedWith;
    },
    get starts() {
      return starts;
    },
    get stops() {
      return stops;
    },
    get hasTicker() {
      return tick !== null;
    }
  };
}

describe('z-order 守卫的节奏', () => {
  it('轮询间隔就是用户看到的闪烁时长：30ms，慢刷新那一格是 10 拍', () => {
    // 200ms 时"点任务栏闪一下"就是守卫在捞它；压到 30ms 后实测恢复 13ms / 20ms。
    // 这两个数被改坏不会报错，只会把闪烁放回到用户脸上，所以钉在这里。
    expect(Z_GUARD_INTERVAL_MS).toBe(30);
    expect(Z_ROW_REFRESH_TICKS).toBe(10);
    const h = harness();
    h.guard.start();
    expect(h.startedWith).toBe(Z_GUARD_INTERVAL_MS);
  });

  it('start / stop 幂等：不会叠两个定时器，也不会漏取消', () => {
    const h = harness();
    h.guard.start();
    h.guard.start();
    expect(h.starts).toBe(1);
    h.guard.stop();
    h.guard.stop();
    expect(h.stops).toBe(1);
    expect(h.guard.running).toBe(false);
  });

  it('stop 之后拍子断掉：什么都不再问', () => {
    const h = harness();
    h.source.inRow = true;
    h.source.isCovered = true;
    h.guard.start();
    h.poll(2);
    h.guard.stop();
    const before = h.source.covers;
    h.poll(5);
    expect(h.source.covers).toBe(before);
    expect(h.hasTicker).toBe(false);
  });

  it('间隔与慢刷拍数可覆盖（量具与复现要用别的档位试）', () => {
    const h = harness({ intervalMs: 60, refreshTicks: 2 });
    h.guard.start();
    expect(h.startedWith).toBe(60);
    h.poll(5);
    expect(h.source.refreshes).toBe(3); // 第 0、2、4 拍
  });
});

describe('z-order 守卫每拍问了什么', () => {
  it('不在行内时连遮挡判定都不问（悬浮态不付守卫的钱）', () => {
    // 这条短路是实测出来的：写成 need(a, costly()) 就等于没短路，
    // 悬浮态会从「与基线不可区分」涨回 1.98% 单核。
    const h = harness();
    h.guard.start();
    h.poll(25);
    expect(h.source.covers).toBe(0);
    expect(h.reasserts).toBe(0);
  });

  it('窗口没了就连慢刷新都不做', () => {
    const h = harness();
    h.source.alive = false;
    h.guard.start();
    h.poll(3);
    expect(h.source.refreshes).toBe(0);
    expect(h.source.covers).toBe(0);
  });

  it('行内判定与句柄按 refreshTicks 慢刷，遮挡判定每拍都问', () => {
    const h = harness();
    h.source.inRow = true;
    h.guard.start();
    h.poll(21);
    expect(h.source.refreshes).toBe(3); // 第 0、10、20 拍
    expect(h.source.covers).toBe(21);
  });

  it('句柄对守卫透明：refresh 给什么，covered 就原样收到什么', () => {
    const h = harness();
    h.source.inRow = true;
    h.guard.start();
    h.poll(1);
    expect(h.source.lastCoverTaskbar).toBe('HWND-7');
  });
});

describe('z-order 守卫什么时候重申档位', () => {
  it('行内 + 被任务栏盖住才重申', () => {
    const h = harness();
    h.source.inRow = true;
    h.source.isCovered = true;
    h.guard.start();
    h.poll(3);
    expect(h.reasserts).toBe(3);
  });

  it('行内但没被盖住不动作（不跟正常 z-order 抢）', () => {
    const h = harness();
    h.source.inRow = true;
    h.guard.start();
    h.poll(3);
    expect(h.reasserts).toBe(0);
  });

  it('行内状态最多滞后一拍慢刷新，滞后期内仍每拍捞回', () => {
    // 缓存是要付出的代价：拖出行内后最长 300ms 仍按行内处理（每拍多一次遮挡判定 + 重申）。
    // 行内状态只可能因拖动或显示器变化而改变，两者都会走 redock/重判，所以这个滞后不构成问题
    // —— 但它必须是「到点就改判」，不能一路挂着。
    const h = harness();
    h.source.inRow = true;
    h.source.isCovered = true;
    h.guard.start();
    h.poll(1);
    expect(h.reasserts).toBe(1);
    h.source.inRow = false;
    h.poll(9);
    expect(h.reasserts).toBe(10);
    expect(h.source.refreshes).toBe(1);
    h.poll(1); // 第 10 拍：到刷新点，改判为行外
    expect(h.source.refreshes).toBe(2);
    expect(h.reasserts).toBe(10);
    expect(h.source.covers).toBe(10);
    h.poll(10);
    expect(h.reasserts).toBe(10);
    expect(h.source.covers).toBe(10);
  });
});
