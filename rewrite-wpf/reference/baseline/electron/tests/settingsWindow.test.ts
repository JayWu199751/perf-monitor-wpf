import { describe, expect, it, vi } from 'vitest';
import {
  SETTINGS_CONTENT_HEIGHT,
  SETTINGS_MIN_HEIGHT,
  createSettingsWindowController,
  settingsWindowHeight,
  type SettingsWindowLike
} from '../src/main/settingsWindow';

interface FakeWindow extends SettingsWindowLike {
  readonly id: number;
  close(): void;
  markClosed(): void;
  calls: string[];
}

function harness(opts?: { idleDestroyMs?: number; isQuitting?: () => boolean }) {
  let nextId = 1;
  const created: FakeWindow[] = [];
  const pending: Array<() => void> = [];
  let cancelled = 0;

  const makeWindow = (): FakeWindow => {
    const calls: string[] = [];
    let destroyed = false;
    let closedFired = false;
    const closeHandlers: Array<(e: { preventDefault(): void }) => void> = [];
    const closedHandlers: Array<() => void> = [];
    const win: FakeWindow = {
      id: nextId++,
      calls,
      show: () => void calls.push('show'),
      focus: () => void calls.push('focus'),
      hide: () => void calls.push('hide'),
      destroy: () => {
        calls.push('destroy');
        destroyed = true;
        if (!closedFired) {
          closedFired = true;
          for (const h of closedHandlers) h();
        }
      },
      isDestroyed: () => destroyed,
      on: ((event: string, handler: never) => {
        if (event === 'close') closeHandlers.push(handler as never);
        if (event === 'closed') closedHandlers.push(handler as never);
      }) as FakeWindow['on'],
      // 模拟用户点关闭按钮
      close: () => {
        let prevented = false;
        for (const h of closeHandlers) h({ preventDefault: () => (prevented = true) });
        if (!prevented) win.destroy();
      },
      markClosed: () => {
        closedFired = true;
        for (const h of closedHandlers) h();
      }
    };
    created.push(win);
    return win;
  };

  const controller = createSettingsWindowController({
    createWindow: makeWindow,
    isQuitting: opts?.isQuitting ?? (() => false),
    idleDestroyMs: opts?.idleDestroyMs ?? 60_000,
    schedule: (fn) => {
      pending.push(fn);
      return () => {
        cancelled++;
        pending.length = 0;
      };
    }
  });

  return {
    controller,
    created,
    pending,
    cancelledCount: () => cancelled,
    fireIdle: () => pending.splice(0).forEach((fn) => fn())
  };
}

describe('设置窗生命周期', () => {
  it('首次打开创建窗口并显示聚焦', () => {
    const h = harness();
    h.controller.open();
    expect(h.created).toHaveLength(1);
    expect(h.created[0].calls).toEqual(['show', 'focus']);
  });

  it('隐藏期间再打开复用同一实例', () => {
    const h = harness();
    h.controller.open();
    h.created[0].close();
    h.controller.open();
    expect(h.created).toHaveLength(1);
    expect(h.created[0].calls).toEqual(['show', 'focus', 'hide', 'show', 'focus']);
  });

  it('关闭只隐藏不销毁，并排上闲置销毁定时器', () => {
    const h = harness();
    h.controller.open();
    h.created[0].close();
    expect(h.created[0].calls).toEqual(['show', 'focus', 'hide']);
    expect(h.pending).toHaveLength(1);
  });

  it('闲置到点销毁渲染进程', () => {
    const h = harness();
    h.controller.open();
    h.created[0].close();
    h.fireIdle();
    expect(h.created[0].calls).toContain('destroy');
    expect(h.created[0].isDestroyed()).toBe(true);
  });

  it('闲置期内重新打开则取消销毁', () => {
    const h = harness();
    h.controller.open();
    h.created[0].close();
    h.controller.open();
    expect(h.pending).toHaveLength(0);
    expect(h.cancelledCount()).toBe(1);
    h.fireIdle();
    expect(h.created[0].isDestroyed()).toBe(false);
  });

  it('销毁之后再打开重新创建实例', () => {
    const h = harness();
    h.controller.open();
    h.created[0].close();
    h.fireIdle();
    h.controller.open();
    expect(h.created).toHaveLength(2);
    expect(h.created[1].calls).toEqual(['show', 'focus']);
  });

  it('退出流程中 close 放行，不再拦截', () => {
    let quitting = false;
    const h = harness({ isQuitting: () => quitting });
    h.controller.open();
    quitting = true;
    h.created[0].close();
    // 放行 => close 自己把窗口关了，且没有 hide、没有排定时器
    expect(h.created[0].calls).toContain('destroy');
    expect(h.created[0].calls).not.toContain('hide');
    expect(h.pending).toHaveLength(0);
  });

  it('idleDestroyMs <= 0 时退回只隐藏不销毁', () => {
    const h = harness({ idleDestroyMs: 0 });
    h.controller.open();
    h.created[0].close();
    expect(h.pending).toHaveLength(0);
    h.fireIdle();
    expect(h.created[0].isDestroyed()).toBe(false);
  });

  it('dispose 撤掉待触发的定时器', () => {
    const h = harness();
    h.controller.open();
    h.created[0].close();
    h.controller.dispose();
    expect(h.pending).toHaveLength(0);
    expect(h.controller.hasWindow).toBe(false);
  });

  it('窗口被外部关闭时状态自愈，下次打开重建', () => {
    const h = harness();
    h.controller.open();
    h.created[0].markClosed();
    expect(h.controller.hasWindow).toBe(false);
    h.controller.open();
    expect(h.created).toHaveLength(2);
  });

  it('重复 open 不会叠加 close 处理器', () => {
    const spy = vi.fn();
    const h = harness();
    h.controller.open();
    h.controller.open();
    h.created[0].close();
    spy();
    // 只 hide 一次说明 close 只挂了一个处理器
    expect(h.created[0].calls.filter((c) => c === 'hide')).toHaveLength(1);
  });
});

describe('settingsWindowHeight', () => {
  // 表现被守在两处：一屏要装得下整页（内容 850 + 余量），装不下时必须退成「工作区 − 余量」
  // 而不是硬撑出屏——窗口 resizable:false，算错了用户就只能靠滚动条找最后一张卡。
  it('工作区够高时按内容高开，一屏装下整页', () => {
    expect(settingsWindowHeight(1080)).toBe(SETTINGS_CONTENT_HEIGHT);
    // 开发机：2048×1152 @125% → 工作区 921 CSS px，装得下 832 的整页内容，
    // 于是只退到「921 − 48」而不是缩内容。
    expect(settingsWindowHeight(921)).toBe(921 - 48);
  });

  it('工作区不够高时退到「工作区 − 余量」，由滚动条兜底', () => {
    expect(settingsWindowHeight(800)).toBe(752);
    expect(settingsWindowHeight(600)).toBe(SETTINGS_MIN_HEIGHT);
  });
});
