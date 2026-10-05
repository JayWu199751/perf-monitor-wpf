import { describe, expect, it, vi } from 'vitest';
import { createSettingsHub } from '../src/main/settingsHub';
import { DEFAULT_SETTINGS } from '../src/main/settings';
import type { Settings } from '../src/shared/types';

function harness(isPackaged = true) {
  const written: Settings[] = [];
  const broadcast: Settings[] = [];
  const log: string[] = [];
  const effects = {
    setAutostart: vi.fn(() => false),
    cleanupLegacyAutostart: vi.fn(),
    restartMetrics: vi.fn(),
    applyTheme: vi.fn(),
    setFullscreenWatch: vi.fn(),
    applyCenteringRule: vi.fn(() => {
      log.push('center:' + hub.get().centerInTaskbarRow);
    })
  };
  const hub = createSettingsHub({
    initial: structuredClone(DEFAULT_SETTINGS), effects, env: { isPackaged },
    write: (s) => { written.push(s); log.push('write'); },
    broadcast: (s) => { broadcast.push(s); log.push('broadcast'); }
  });
  return { hub, effects, written, broadcast, log };
}

describe('设置更新 module 的完整流程', () => {
  it('读取不可变快照，更新不改写历史快照或调用方输入', () => {
    const h = harness();
    const initial = h.hub.get();
    expect(() => { (initial as Settings).widget.x = 10; }).toThrow();
    expect(() => { (initial as Settings).theme = 'dark'; }).toThrow();
    expect(() => { (initial as Settings).metrics.cpu = false; }).toThrow();
    const patch = { fontSize: 16, metrics: { net: false } };
    const next = h.hub.applyPatch(patch);
    patch.metrics.net = true;
    expect(initial.fontSize).toBe(12);
    expect(next.fontSize).toBe(16);
    expect(next.metrics.net).toBe(false);
    expect(h.written).toEqual([next]);
    expect(h.broadcast[0]).toBe(h.written[0]);
    expect(h.hub.get()).toBe(next);
  });

  it('设置窗只改允许的项，副作用结果写回后一起提交', () => {
    const h = harness();
    const next = h.hub.applyPatch({
      theme: 'dark', autostart: true, refreshFastMs: 2000,
      metrics: { net: false }, widget: { x: 123 },
      ...{ centerInTaskbarRow: true, transparentDisplay: true }
    });
    expect(next.widget).toEqual(DEFAULT_SETTINGS.widget);
    expect(next.centerInTaskbarRow).toBe(false);
    expect(next.transparentDisplay).toBe(false);
    expect(next.autostart).toBe(false);
    expect(next.metrics).toEqual({ ...DEFAULT_SETTINGS.metrics, net: false });
    expect(h.effects.applyTheme).toHaveBeenCalledWith('dark');
    expect(h.effects.restartMetrics).toHaveBeenCalledWith(expect.objectContaining({ refreshFastMs: 2000 }));
    expect(h.written).toHaveLength(1);
    expect(h.broadcast).toEqual(h.written);
  });

  it('共享菜单提交后立即应用行内居中，重复赋值不产生动作', () => {
    const h = harness();
    h.hub.setCenterInTaskbarRow(true);
    expect(h.log).toEqual(['write', 'broadcast', 'center:true']);
    h.hub.setCenterInTaskbarRow(true);
    expect(h.effects.applyCenteringRule).toHaveBeenCalledTimes(1);
    h.hub.setTransparentDisplay(true);
    expect(h.hub.get().transparentDisplay).toBe(true);
    h.hub.setTransparentDisplay(true);
    expect(h.written).toHaveLength(2);
    expect(h.effects.applyCenteringRule).toHaveBeenCalledTimes(1);
  });

  it('位置只更新内存，flush 一次写当前快照，不广播', () => {
    const h = harness();
    const position = { x: 551, y: 455, docked: null };
    h.hub.stagePosition(position);
    position.x = 0;
    expect(h.hub.get().widget.x).toBe(551);
    expect(h.written).toHaveLength(0);
    h.hub.flushPosition();
    h.hub.flushPosition();
    expect(h.written).toEqual([h.hub.get()]);
    expect(h.broadcast).toHaveLength(0);
  });

  it('防抖期间用户修改设置会一并持久化最新位置，之后 flush 不重复写', () => {
    const h = harness();
    h.hub.stagePosition({ x: 1, y: 2, docked: 'top' });
    h.hub.applyPatch({ opacity: 0.5 });
    h.hub.flushPosition();
    expect(h.written).toHaveLength(1);
    expect(h.written[0]).toMatchObject({ opacity: 0.5, widget: { x: 1, y: 2 } });
    h.hub.stagePosition({ x: 3, y: 4, docked: null });
    h.hub.flushPosition();
    expect(h.written.at(-1)).toMatchObject({ opacity: 0.5, widget: { x: 3, y: 4 } });
    expect(h.broadcast).toHaveLength(1);
  });

  it('启动自启纠正静默提交，未纠正不落盘', () => {
    const h = harness();
    h.hub.reconcileOnBoot();
    expect(h.effects.cleanupLegacyAutostart).toHaveBeenCalledOnce();
    expect(h.written).toHaveLength(0);
    h.effects.setAutostart.mockReturnValueOnce(true);
    h.hub.applyPatch({ autostart: true });
    h.written.length = 0;
    h.broadcast.length = 0;
    h.hub.reconcileOnBoot();
    expect(h.hub.get().autostart).toBe(false);
    expect(h.written).toEqual([h.hub.get()]);
    expect(h.broadcast).toHaveLength(0);
  });

  it('dev 不动系统自启，重复位置不标记待写盘', () => {
    const h = harness(false);
    h.hub.reconcileOnBoot();
    h.hub.applyPatch({ autostart: true });
    expect(h.effects.setAutostart).not.toHaveBeenCalled();
    expect(h.hub.get().autostart).toBe(false);
    expect(h.hub.stagePosition({ ...h.hub.get().widget })).toBe(false);
    h.hub.flushPosition();
    expect(h.written).toHaveLength(1);
  });
});
