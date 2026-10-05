import type { DockMask } from '../shared/types';
import type { ContentSize, Rect } from './geometry';
import type { SettingsHub } from './settingsHub';
import {
  dockedPosition, edgesToMask, nearestEdges, windowSizingFor,
  type DisplayRects
} from './dock';
import { attachMoveState } from './moveState';
import { createAutoHideController } from './autoHide';
import { createZOrderGuard, type TaskbarSource } from './zOrderGuard';

type WidgetZLevel = 'pop-up-menu';
// Win11 的透明窗口此档位是实测能盖住任务栏的最低档；档位与守卫节奏遵循 ADR-0005。
const WIDGET_Z_LEVEL: WidgetZLevel = 'pop-up-menu';
export const POSITION_SAVE_DEBOUNCE_MS = 500;

export interface WidgetWindowLike {
  isDestroyed(): boolean;
  isVisible(): boolean;
  show(): void;
  hide(): void;
  focus(): void;
  setAlwaysOnTop(flag: boolean, level: WidgetZLevel): void;
  hookWindowMessage(message: number, callback: () => void): void;
  webContents: {
    send(channel: string, payload: unknown): void;
    on(event: 'context-menu', callback: () => void): void;
  };
  on(event: 'ready-to-show' | 'show' | 'hide' | 'closed' | 'close' | 'moved', handler: () => void): void;
  on(event: 'system-context-menu', handler: (e: { preventDefault(): void }) => void): void;
}

export interface WidgetDisplayEvents {
  onChange(cb: () => void): () => void;
}

/** 窗口能力 seam：adapter 只映射真实矩形、显示器与摆窗，不再反查 controller 状态。 */
export interface WidgetWindowPorts {
  docking: {
    getBounds(): Rect;
    displayFor(bounds: Rect): DisplayRects;
    setBounds(bounds: Rect): void;
  };
  taskbar: TaskbarSource<bigint | null>;
  displayEvents: WidgetDisplayEvents;
}

export interface WidgetWindowOptions {
  createWindow(): WidgetWindowLike;
  ports(win: WidgetWindowLike): WidgetWindowPorts;
  settings: Pick<SettingsHub, 'get' | 'stagePosition' | 'flushPosition'>;
  metricsControls: {
    setPaused(paused: boolean): void;
    setMoving(moving: boolean): void;
  };
  startTimer(tick: () => void, intervalMs: number): () => void;
  /** 一次性防抖计时器；返回取消函数。 */
  schedule(callback: () => void, delayMs: number): () => void;
  popupMenu(win: WidgetWindowLike): void;
}

/** 小窗 module 拥有可见性、真实尺寸落位、移动结算与保存时序，不依赖 electron。 */
export function createWidgetWindowController(opts: WidgetWindowOptions) {
  const win = opts.createWindow();
  const { docking, taskbar, displayEvents } = opts.ports(win);
  let disposed = false;
  const active = (): boolean => !disposed && !win.isDestroyed();
  const reassert = (): void => {
    if (active()) win.setAlwaysOnTop(true, WIDGET_Z_LEVEL);
  };
  reassert();
  const zGuard = createZOrderGuard({ source: taskbar, reassert, startTimer: opts.startTimer });

  let contentSize: ContentSize | null = null;
  let positionRestored = false;
  let moving = false;
  // 屏蔽 setBounds 同步派发的 moved 重入，不靠迭代求解去消格点偏差。
  let placing = false;
  let cancelSave: (() => void) | null = null;

  function flushPosition(): void {
    cancelSave?.();
    cancelSave = null;
    opts.settings.flushPosition();
  }

  function scheduleSave(): void {
    cancelSave?.();
    cancelSave = opts.schedule(flushPosition, POSITION_SAVE_DEBOUNCE_MS);
  }

  /**
   * 唯一落位路径：落请求尺寸 → 回读真矩形 → 求解 → 含请求尺寸落位。
   * 求解绝不预测 OS 钳制；请求尺寸不能换成窗口现尺寸，否则外框高度会逐次累加。
   * redetect 只在结算时重新判掩码；恢复与尺寸/显示器重摆保留已存掩码。
   */
  function placeWidget(mask: DockMask | null, position: { x: number; y: number } | null = null,
    redetect = false): void {
    if (!active() || moving || placing) return;
    placing = true;
    try {
      const before = docking.getBounds();
      const request = windowSizingFor(before, contentSize);
      if (position || request.width !== before.width || request.height !== before.height) {
        docking.setBounds({
          x: position?.x ?? before.x, y: position?.y ?? before.y, ...request
        });
      }
      const actual = docking.getBounds();
      const display = docking.displayFor(actual);
      if (redetect) mask = edgesToMask(nearestEdges(actual, display.workArea));
      const pos = dockedPosition({
        bounds: actual, display, mask, content: contentSize,
        centerInRow: opts.settings.get().centerInTaskbarRow
      });
      if (pos.x !== actual.x || pos.y !== actual.y) docking.setBounds({ ...pos, ...request });
      if (redetect || positionRestored) {
        const final = docking.getBounds();
        if (opts.settings.stagePosition({ x: final.x, y: final.y, docked: mask })) scheduleSave();
      }
    } finally {
      placing = false;
    }
  }

  const redock = (): void => {
    if (!positionRestored) return;
    placeWidget(opts.settings.get().widget.docked);
  };
  const settle = (): void => {
    if (!positionRestored) return;
    placeWidget(null, null, true);
  };

  const autoHide = createAutoHideController({
    isVisible: () => active() && win.isVisible(),
    hide: () => { if (active()) win.hide(); },
    show: () => { if (active()) win.show(); },
    focus: () => { if (active()) win.focus(); }
  });

  win.on('moved', settle);
  win.on('close', flushPosition);
  win.on('ready-to-show', () => {
    if (!active()) return;
    const { x, y, docked } = opts.settings.get().widget;
    // 恢复之前的 moved/尺寸/显示器事件均不能结算工厂位置（ADR-0005 修正四）。
    placeWidget(docked, x || y ? { x, y } : null);
    positionRestored = true;
    settle();
    win.show();
  });
  win.on('show', () => {
    if (!active()) return;
    opts.metricsControls.setPaused(false);
    reassert();
    zGuard.start();
    redock();
  });
  win.on('hide', () => {
    opts.metricsControls.setPaused(true);
    zGuard.stop();
  });

  attachMoveState(win, (isMoving) => {
    if (!active()) return;
    moving = isMoving;
    opts.metricsControls.setMoving(isMoving);
    if (!isMoving) settle();
  });
  const unsubscribeDisplay = displayEvents.onChange(() => {
    redock();
    settle();
  });

  function dispose(): void {
    if (disposed) return;
    flushPosition();
    disposed = true;
    zGuard.stop();
    unsubscribeDisplay();
  }
  win.on('closed', dispose);

  // 两条右键路径共用去重规则；初值 -∞ 保证假时钟 0 上首次右键也可打开。
  let lastPopupAt = Number.NEGATIVE_INFINITY;
  const popupAppMenu = (): void => {
    if (!active()) return;
    const now = Date.now();
    if (now - lastPopupAt < 300) return;
    lastPopupAt = now;
    opts.popupMenu(win);
  };
  win.webContents.on('context-menu', popupAppMenu);
  win.on('system-context-menu', (e) => {
    e.preventDefault();
    popupAppMenu();
  });

  return {
    toggle: () => autoHide.onManualToggle(),
    reveal: () => autoHide.onManualShow(),
    onFullscreenChange: (full: boolean) => autoHide.onFullscreenChange(full),
    handleContentResize(width: number, height: number): void {
      if (!active()) return;
      contentSize = { width: Math.round(width), height: Math.round(height) };
      redock();
    },
    applyCenteringRule: settle,
    sendToRenderer(channel: string, payload: unknown): void {
      if (active()) win.webContents.send(channel, payload);
    },
    flushPosition,
    dispose
  };
}

export type WidgetWindowController = ReturnType<typeof createWidgetWindowController>;
