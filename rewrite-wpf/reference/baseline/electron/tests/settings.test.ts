import { describe, expect, it, vi } from 'vitest';
import {
  DEFAULT_SETTINGS,
  applySettingsPatch,
  fillDefaultSettings,
  reconcileAutostartOnBoot,
  type SettingsEffects,
  type SettingsPatch
} from '../src/main/settings';
import type { Settings } from '../src/shared/types';

function effects(overrides: Partial<SettingsEffects> = {}): SettingsEffects {
  return {
    setAutostart: vi.fn(() => true),
    cleanupLegacyAutostart: vi.fn(),
    restartMetrics: vi.fn(),
    applyTheme: vi.fn(),
    setFullscreenWatch: vi.fn(),
    ...overrides
  };
}

const PACKAGED = { isPackaged: true };
const DEV = { isPackaged: false };

describe('fillDefaultSettings', () => {
  it('tolerates an empty config file', () => {
    expect(fillDefaultSettings({})).toEqual(DEFAULT_SETTINGS);
  });

  it('fills missing keys inside nested objects without dropping the rest', () => {
    const filled = fillDefaultSettings({
      widget: { x: 120 },
      metrics: { gpu: false },
      opacity: 0.5
    });
    expect(filled.widget).toEqual({ x: 120, y: 0, docked: null });
    expect(filled.metrics).toEqual({ cpu: true, mem: true, gpu: false, net: true, time: true });
    expect(filled.opacity).toBe(0.5);
  });

  it('旧配置缺 centerInTaskbarRow 时补 false，磁盘上写了的照收', () => {
    // 行内居中是后加的键：手改过的旧配置缺它 = 关闭，"零影响升级"的默认值
    expect(fillDefaultSettings({ theme: 'dark' }).centerInTaskbarRow).toBe(false);
    expect(fillDefaultSettings({ centerInTaskbarRow: true }).centerInTaskbarRow).toBe(true);
  });
});

describe('applySettingsPatch', () => {
  it('drops widget patches — the main process owns the widget position', () => {
    const current: Settings = { ...DEFAULT_SETTINGS, widget: { x: 42, y: 7, docked: 'top-left' } };
    const next = applySettingsPatch(current, { widget: { x: 999, y: 999 } }, effects(), PACKAGED);
    expect(next.widget).toEqual({ x: 42, y: 7, docked: 'top-left' });
  });

  it('drops centerInTaskbarRow patches — 唯一入口是共享菜单，绕过它落盘会让 checkbox 与实态漂移', () => {
    // SettingsPatch 类型上已不可发该键；cast 模拟 IPC 边界（JSON，不带类型）发来的补丁
    const off: SettingsPatch = { centerInTaskbarRow: false } as unknown as SettingsPatch;
    const on: SettingsPatch = { centerInTaskbarRow: true } as unknown as SettingsPatch;
    const current: Settings = { ...DEFAULT_SETTINGS, centerInTaskbarRow: true };
    expect(applySettingsPatch(current, off, effects(), PACKAGED).centerInTaskbarRow).toBe(true);
    expect(applySettingsPatch(DEFAULT_SETTINGS, on, effects(), PACKAGED).centerInTaskbarRow).toBe(false);
  });

  it('deep merges metrics so the renderer can send one key', () => {
    const next = applySettingsPatch(DEFAULT_SETTINGS, { metrics: { net: false } }, effects(), PACKAGED);
    expect(next.metrics.net).toBe(false);
    expect(next.metrics.cpu).toBe(true);
  });

  it('restarts sampling with the merged settings when the interval changes', () => {
    const fx = effects();
    applySettingsPatch(DEFAULT_SETTINGS, { refreshFastMs: 2000 }, fx, PACKAGED);
    expect(fx.restartMetrics).toHaveBeenCalledTimes(1);
    // 传入的是合并后的完整设置，避免重建的采样服务读到旧间隔
    expect(vi.mocked(fx.restartMetrics).mock.calls[0]?.[0].refreshFastMs).toBe(2000);
  });

  it('does not restart sampling when an unrelated key changes', () => {
    const fx = effects();
    applySettingsPatch(DEFAULT_SETTINGS, { opacity: 0.9 }, fx, PACKAGED);
    expect(fx.restartMetrics).not.toHaveBeenCalled();
  });

  it('maps theme straight onto the injected effect', () => {
    const fx = effects();
    applySettingsPatch(DEFAULT_SETTINGS, { theme: 'dark' }, fx, PACKAGED);
    expect(fx.applyTheme).toHaveBeenCalledWith('dark');
  });

  it('only reports autostart as enabled when the scheduled task was created', () => {
    const failing = effects({ setAutostart: vi.fn(() => false) });
    const next = applySettingsPatch(DEFAULT_SETTINGS, { autostart: true }, failing, PACKAGED);
    expect(next.autostart).toBe(false);
  });

  it('never writes system autostart in dev', () => {
    const fx = effects();
    const next = applySettingsPatch(DEFAULT_SETTINGS, { autostart: true }, fx, DEV);
    expect(next.autostart).toBe(false);
    expect(fx.setAutostart).not.toHaveBeenCalled();
  });

  it('starts and stops the fullscreen watcher with the toggle', () => {
    const fx = effects();
    applySettingsPatch(DEFAULT_SETTINGS, { autoHideOnFullscreen: true }, fx, PACKAGED);
    expect(fx.setFullscreenWatch).toHaveBeenCalledWith(true);
    applySettingsPatch(DEFAULT_SETTINGS, { autoHideOnFullscreen: false }, fx, PACKAGED);
    expect(fx.setFullscreenWatch).toHaveBeenCalledWith(false);
  });
});

describe('reconcileAutostartOnBoot', () => {
  it('returns the same reference in dev so the caller skips the write', () => {
    const fx = effects();
    expect(reconcileAutostartOnBoot(DEFAULT_SETTINGS, fx, DEV)).toBe(DEFAULT_SETTINGS);
    expect(fx.cleanupLegacyAutostart).not.toHaveBeenCalled();
  });

  it('clears legacy Run entries and re-creates the task when packaged', () => {
    const fx = effects();
    const current: Settings = { ...DEFAULT_SETTINGS, autostart: true };
    expect(reconcileAutostartOnBoot(current, fx, PACKAGED)).toBe(current);
    expect(fx.cleanupLegacyAutostart).toHaveBeenCalledOnce();
    expect(fx.setAutostart).toHaveBeenCalledWith(true);
  });

  it('writes the flag back to false when the task cannot be created', () => {
    const fx = effects({ setAutostart: vi.fn(() => false) });
    const current: Settings = { ...DEFAULT_SETTINGS, autostart: true };
    expect(reconcileAutostartOnBoot(current, fx, PACKAGED).autostart).toBe(false);
  });
});
