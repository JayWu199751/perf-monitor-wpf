import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createWidgetWindowController, POSITION_SAVE_DEBOUNCE_MS,
  type WidgetWindowLike, type WidgetWindowController
} from '../src/main/widgetWindow';
import { createWidgetWindowPorts } from '../src/main/widgetWindowPorts';
import { createSettingsHub } from '../src/main/settingsHub';
import { DEFAULT_SETTINGS } from '../src/main/settings';
import { WM_ENTERSIZEMOVE, WM_EXITSIZEMOVE } from '../src/main/moveState';
import { cardInTaskbarRow } from '../src/main/dock';
import type { Settings } from '../src/shared/types';
import type { Rect } from '../src/main/geometry';

const desktop = vi.hoisted(() => ({
  bounds: { x: 0, y: 0, width: 1920, height: 1080 },
  workArea: { x: 0, y: 40, width: 1920, height: 1040 }
}));
const native = vi.hoisted(() => ({
  point: vi.fn(() => false),
  taskbar: vi.fn(() => null as bigint | null),
  nearest: vi.fn(() => desktop)
}));
vi.mock('electron', () => ({
  screen: {
    getDisplayNearestPoint: native.nearest,
    dipToScreenPoint: (p: { x: number; y: number }) => p,
    on: () => {}, removeListener: () => {}
  }
}));
vi.mock('../src/main/win32', () => ({
  findTaskbarWindow: native.taskbar,
  isPointOwnedByWindow: native.point
}));

beforeEach(() => {
  vi.useFakeTimers();
  desktop.workArea = { x: 0, y: 40, width: 1920, height: 1040 };
  native.point.mockClear();
  native.taskbar.mockReset().mockReturnValue(null);
  native.nearest.mockClear();
});
afterEach(() => { vi.clearAllTimers(); vi.useRealTimers(); });

function harness(options: {
  settings?: Settings; bounds?: Rect; delayedMoved?: boolean; osHeight?: number
} = {}) {
  const events = new Map<string, Array<(...args: any[]) => void>>();
  const messages = new Map<number, () => void>();
  const log: string[] = [];
  const placements: Rect[] = [];
  const written: Settings[] = [];
  const broadcast: Settings[] = [];
  const paused: boolean[] = [];
  const moving: boolean[] = [];
  let bounds = options.bounds ?? { x: 780, y: 521, width: 360, height: 41 };
  let visible = false;
  let destroyed = false;
  let displayChange: (() => void) | null = null;
  let tick: (() => void) | null = null;
  let timerStarts = 0;
  let timerStops = 0;
  let displayUnsubscribed = 0;
  const bind = (event: string, cb: (...args: any[]) => void) => {
    events.set(event, [...(events.get(event) ?? []), cb]);
  };
  const emit = (event: string, ...args: any[]) => {
    for (const cb of events.get(event) ?? []) cb(...args);
  };
  const win = {
    isDestroyed: () => destroyed,
    isVisible: () => visible,
    show: () => { visible = true; log.push('show'); emit('show'); },
    hide: () => { visible = false; log.push('hide'); emit('hide'); },
    focus: () => log.push('focus'),
    setAlwaysOnTop: (_flag: boolean, level: string) => log.push('z:' + level),
    hookWindowMessage: (id: number, cb: () => void) => messages.set(id, cb),
    on: bind as WidgetWindowLike['on'],
    webContents: {
      send: (channel: string, payload: unknown) => log.push('send:' + channel + ':' + JSON.stringify(payload)),
      on: (event: 'context-menu', cb: () => void) => bind('web:' + event, cb)
    },
    getBounds: () => ({ ...bounds }),
    setBounds: (requested: Rect) => {
      placements.push({ ...requested });
      const next = { ...requested, height: Math.max(options.osHeight ?? 38, requested.height) };
      if (JSON.stringify(next) === JSON.stringify(bounds)) return;
      bounds = next;
      if (options.delayedMoved) setTimeout(() => emit('moved'), 0);
      else emit('moved');
    }
  } satisfies WidgetWindowLike & { getBounds(): Rect; setBounds(b: Rect): void };
  let ctrl: WidgetWindowController;
  const hub = createSettingsHub({
    initial: options.settings ?? structuredClone({
      ...DEFAULT_SETTINGS, widget: { x: 430, y: 1, docked: 'top' }
    }),
    write: (s) => written.push(s), broadcast: (s) => broadcast.push(s),
    effects: {
      setAutostart: () => false, cleanupLegacyAutostart: () => {}, restartMetrics: () => {},
      applyTheme: () => {}, setFullscreenWatch: () => {},
      applyCenteringRule: () => ctrl.applyCenteringRule()
    },
    env: { isPackaged: false }
  });
  ctrl = createWidgetWindowController({
    createWindow: () => win,
    ports: (w) => ({
      ...createWidgetWindowPorts(w),
      displayEvents: {
        onChange: (cb) => {
          displayChange = cb;
          return () => { displayChange = null; displayUnsubscribed++; };
        }
      }
    }),
    settings: hub,
    metricsControls: {
      setPaused: (p) => paused.push(p), setMoving: (p) => moving.push(p)
    },
    startTimer: (cb, ms) => {
      expect(ms).toBe(30);
      tick = cb; timerStarts++;
      return () => { tick = null; timerStops++; };
    },
    schedule: (cb, ms) => { const id = setTimeout(cb, ms); return () => clearTimeout(id); },
    popupMenu: () => log.push('popup')
  });
  return {
    ctrl, hub, written, broadcast, paused, moving, log, placements, emit,
    bounds: () => ({ ...bounds }),
    visible: () => visible,
    hooked: () => [...messages.keys()].sort(),
    counters: () => ({ timerStarts, timerStops, displayUnsubscribed }),
    drag: (on: boolean) => messages.get(on ? WM_ENTERSIZEMOVE : WM_EXITSIZEMOVE)?.(),
    move: (next: Partial<Rect>) => { bounds = { ...bounds, ...next }; emit('moved'); },
    display: () => displayChange?.(),
    poll: (n = 1) => { for (let i = 0; i < n; i++) tick?.(); },
    destroy: () => { destroyed = true; emit('closed'); }
  };
}

describe('小窗恢复与真实窗口 adapter', () => {
  const cases = (['top', 'bottom'] as const).flatMap((edge) =>
    [true, false].flatMap((centerInRow) =>
      [true, false].map((resizeFirst) => ({ edge, centerInRow, resizeFirst }))));
  it.each(cases)('恢复 $edge 行内：居中=$centerInRow，尺寸先到=$resizeFirst', ({
    edge, centerInRow, resizeFirst
  }) => {
    desktop.workArea = edge === 'top'
      ? { x: 0, y: 40, width: 1920, height: 1040 }
      : { x: 0, y: 0, width: 1920, height: 1040 };
    const rowY = edge === 'top' ? 1 : 1041;
    const rowX = centerInRow ? 754 : 430;
    const initial: Settings = {
      ...structuredClone(DEFAULT_SETTINGS), centerInTaskbarRow: centerInRow,
      widget: { x: rowX, y: rowY, docked: edge }
    };
    const h = harness({ settings: initial, delayedMoved: true });
    h.emit('moved');
    h.display();
    h.ctrl.applyCenteringRule();
    if (resizeFirst) h.ctrl.handleContentResize(400, 26);
    expect(h.placements).toEqual([]);
    expect(h.hub.get().widget).toEqual(initial.widget);
    h.emit('ready-to-show');
    vi.runAllTimers();
    if (!resizeFirst) {
      h.ctrl.handleContentResize(400, 26);
      vi.runAllTimers();
    }
    expect(cardInTaskbarRow(desktop, h.bounds())).toBe(true);
    expect(h.bounds()).toEqual({ x: rowX, y: rowY, width: 412, height: 38 });
    expect(h.hub.get().widget).toEqual(initial.widget);
    h.ctrl.dispose();
  });

  it('以真实 OS 高度求解，重复 show/尺寸/居中不会累积窗口高', () => {
    const h = harness({ osHeight: 37 });
    h.ctrl.handleContentResize(400, 26);
    h.emit('ready-to-show');
    expect(h.bounds().y).toBe(2);
    expect(h.bounds().height).toBe(37);
    for (let i = 0; i < 3; i++) {
      h.ctrl.reveal();
      h.ctrl.handleContentResize(400, 26);
      h.ctrl.applyCenteringRule();
      expect(h.bounds().height).toBe(37);
    }
    expect(h.placements.every((b) => b.height === 26)).toBe(true);
    expect(h.placements.every((b) => Number.isInteger(b.y))).toBe(true);
  });

  it('setBounds 同步 moved 不重入，启动位置不能被工厂位置覆盖', () => {
    const h = harness();
    h.ctrl.handleContentResize(400, 26);
    h.emit('ready-to-show');
    expect(h.bounds()).toEqual({ x: 430, y: 1, width: 412, height: 38 });
    expect(h.hub.get().widget).toEqual({ x: 430, y: 1, docked: 'top' });
    expect(h.written).toEqual([]);
    expect(h.log.indexOf('show')).toBeGreaterThan(h.log.indexOf('z:pop-up-menu'));
  });

  it('首启无记忆坐标、移除显示器后的位置均夹回当前工作区', () => {
    const h = harness({
      settings: { ...structuredClone(DEFAULT_SETTINGS), widget: { x: 0, y: 0, docked: null } },
      bounds: { x: 3000, y: 2000, width: 360, height: 38 }
    });
    h.emit('ready-to-show');
    // 贴边允许透明边距出屏，可视卡片留在屏幕内。
    expect(h.bounds().x + h.bounds().width - 6).toBeLessThanOrEqual(1920);
    expect(h.bounds().y + h.bounds().height / 2).toBeLessThan(1080);
    expect(h.visible()).toBe(true);
  });

  it('窗口中心决定显示器，卡片高度变化保留贴边与请求尺寸', () => {
    const h = harness();
    h.emit('ready-to-show');
    h.ctrl.handleContentResize(491.4, 20.6);
    expect(h.bounds().width).toBe(503);
    expect(h.placements.at(-1)?.height).toBe(21);
    h.ctrl.reveal();
    expect(native.nearest).toHaveBeenCalledWith({
      x: Math.round(h.bounds().x + h.bounds().width / 2),
      y: Math.round(h.bounds().y + h.bounds().height / 2)
    });
  });
});

describe('贴边结算、防抖与设置交错', () => {
  it('连续移动只在最后一次的防抖结束后写一次最新位置', () => {
    const h = harness();
    h.emit('ready-to-show');
    h.move({ x: 500, y: 300 });
    expect(h.hub.get().widget).toEqual({ x: 500, y: 300, docked: null });
    vi.advanceTimersByTime(POSITION_SAVE_DEBOUNCE_MS - 1);
    expect(h.written).toEqual([]);
    h.move({ x: 550, y: 320 });
    vi.advanceTimersByTime(POSITION_SAVE_DEBOUNCE_MS - 1);
    expect(h.written).toEqual([]);
    vi.advanceTimersByTime(1);
    expect(h.written).toHaveLength(1);
    expect(h.written[0].widget).toEqual({ x: 550, y: 320, docked: null });
    expect(h.broadcast).toEqual([]);
  });

  it.each(['close', 'flush', 'dispose', 'closed'])('%s 提前保存最后位置并撤掉待写定时器', (action) => {
    const h = harness();
    h.emit('ready-to-show');
    h.move({ x: 500, y: 300 });
    if (action === 'flush') h.ctrl.flushPosition();
    else if (action === 'dispose') h.ctrl.dispose();
    else if (action === 'closed') h.destroy();
    else h.emit('close');
    expect(h.written).toHaveLength(1);
    vi.advanceTimersByTime(POSITION_SAVE_DEBOUNCE_MS * 2);
    expect(h.written).toHaveLength(1);
    expect(h.written[0].widget.x).toBe(500);
  });

  it('拖动暂停采样且不摆窗，尺寸/显示器变化留到松手一次结算', () => {
    const h = harness();
    h.emit('ready-to-show');
    h.ctrl.flushPosition();
    h.written.length = 0;
    h.placements.length = 0;
    h.drag(true);
    h.move({ x: 500, y: 300 });
    h.ctrl.handleContentResize(400, 26);
    h.display();
    h.ctrl.applyCenteringRule();
    vi.advanceTimersByTime(1000);
    expect(h.placements).toEqual([]);
    expect(h.written).toEqual([]);
    h.drag(false);
    expect(h.moving).toEqual([true, false]);
    expect(h.bounds()).toEqual({ x: 500, y: 300, width: 412, height: 38 });
    vi.advanceTimersByTime(POSITION_SAVE_DEBOUNCE_MS);
    expect(h.written).toHaveLength(1);
  });

  it('待写位置期间修改设置，防抖结束不覆盖设置，也不额外落盘', () => {
    const h = harness();
    h.emit('ready-to-show');
    h.move({ x: 500, y: 300 });
    h.hub.applyPatch({ opacity: 0.4 });
    vi.advanceTimersByTime(POSITION_SAVE_DEBOUNCE_MS);
    expect(h.written).toHaveLength(1);
    expect(h.written[0]).toMatchObject({ opacity: 0.4, widget: { x: 500, y: 300 } });
    h.move({ x: 550 });
    h.ctrl.flushPosition();
    expect(h.written.at(-1)).toMatchObject({ opacity: 0.4, widget: { x: 550 } });
    expect(h.broadcast).toHaveLength(1);
  });

  it('行内居中菜单经设置 module 立即重摆且随后保存真位置', () => {
    const h = harness();
    h.ctrl.handleContentResize(400, 26);
    h.emit('ready-to-show');
    h.hub.setCenterInTaskbarRow(true);
    expect(h.bounds().x).toBe(754);
    expect(h.hub.get().widget.x).toBe(754);
    h.move({ x: 430 });
    expect(h.bounds().x).toBe(754);
    h.hub.setCenterInTaskbarRow(false);
    expect(h.bounds().x).toBe(754);
    h.move({ x: 430 });
    expect(h.bounds().x).toBe(430);
    vi.advanceTimersByTime(POSITION_SAVE_DEBOUNCE_MS);
    expect(h.written.at(-1)?.widget.x).toBe(430);
  });

  it('显示器工作区变化后重摆，dispose 只解绑一次且后续事件不动作', () => {
    const h = harness();
    h.emit('ready-to-show');
    desktop.workArea = { x: 0, y: 0, width: 1920, height: 1080 };
    h.display();
    expect(h.bounds().y).toBeLessThanOrEqual(0);
    h.ctrl.dispose();
    h.ctrl.dispose();
    const count = h.placements.length;
    h.display();
    h.emit('moved');
    expect(h.placements).toHaveLength(count);
    expect(h.counters().displayUnsubscribed).toBe(1);
  });
});

describe('小窗显隐与 z-order 不变量', () => {
  it('创建档位与原生拖动消息保持既有约束', () => {
    const h = harness();
    expect(h.log).toEqual(['z:pop-up-menu']);
    expect(h.hooked()).toEqual([WM_ENTERSIZEMOVE, WM_EXITSIZEMOVE].sort());
  });

  it('show 重申档位并启动守卫，hide 暂停采样并停表', () => {
    const h = harness();
    h.emit('ready-to-show');
    expect(h.paused).toEqual([false]);
    expect(h.counters().timerStarts).toBe(1);
    h.ctrl.toggle();
    expect(h.paused).toEqual([false, true]);
    expect(h.counters().timerStops).toBe(1);
    h.ctrl.reveal();
    expect(h.paused).toEqual([false, true, false]);
    expect(h.log.at(-1)).toBe('focus');
    h.destroy();
    expect(h.counters().timerStops).toBe(2);
  });

  it('守卫悬浮时不问遮挡，行内被覆盖才重申档位', () => {
    const h = harness();
    h.emit('ready-to-show');
    h.move({ x: 500, y: 300 });
    h.log.length = 0;
    h.poll();
    expect(native.point).not.toHaveBeenCalled();
    native.taskbar.mockReturnValue(7n);
    native.point.mockReturnValue(true);
    h.move({ x: 430, y: 1 });
    h.poll(10);
    expect(h.log).toEqual(['z:pop-up-menu']);
    h.ctrl.toggle();
    h.log.length = 0;
    h.poll(10);
    expect(h.log).toEqual([]);
  });

  it('自动隐藏可恢复，手动隐藏优先，reveal 清除隐藏状态', () => {
    const h = harness();
    h.emit('ready-to-show');
    h.ctrl.onFullscreenChange(true);
    expect(h.visible()).toBe(false);
    h.ctrl.onFullscreenChange(false);
    expect(h.visible()).toBe(true);
    h.ctrl.toggle();
    h.ctrl.onFullscreenChange(true);
    h.ctrl.onFullscreenChange(false);
    expect(h.visible()).toBe(false);
    h.ctrl.reveal();
    expect(h.visible()).toBe(true);
    const count = h.log.filter((v) => v === 'show').length;
    h.ctrl.onFullscreenChange(false);
    expect(h.log.filter((v) => v === 'show')).toHaveLength(count);
  });

  it('右键路径阻止系统菜单，首次时间为 0 可弹，300ms 内去重', () => {
    const h = harness();
    const preventDefault = vi.fn();
    h.emit('system-context-menu', { preventDefault });
    h.emit('web:context-menu');
    expect(preventDefault).toHaveBeenCalledOnce();
    expect(h.log.filter((v) => v === 'popup')).toHaveLength(1);
    vi.advanceTimersByTime(301);
    h.emit('web:context-menu');
    expect(h.log.filter((v) => v === 'popup')).toHaveLength(2);
  });

  it('渲染层消息可投递，销毁后任何入口均不碰窗口', () => {
    const h = harness();
    h.ctrl.sendToRenderer('metrics', { cpuPct: 12 });
    expect(h.log.at(-1)).toBe('send:metrics:{"cpuPct":12}');
    h.destroy();
    h.log.length = 0;
    h.emit('ready-to-show');
    h.ctrl.reveal();
    h.ctrl.toggle();
    h.ctrl.handleContentResize(400, 26);
    h.ctrl.applyCenteringRule();
    h.ctrl.sendToRenderer('settings', {});
    h.ctrl.onFullscreenChange(true);
    h.display();
    h.drag(false);
    h.poll();
    expect(h.log).toEqual([]);
  });
});
