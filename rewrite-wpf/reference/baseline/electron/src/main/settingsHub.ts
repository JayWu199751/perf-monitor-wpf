import type { Settings } from '../shared/types';
import {
  applySettingsPatch, reconcileAutostartOnBoot,
  type SettingsEffects, type SettingsEnv, type SettingsPatch
} from './settings';

/** 读取只交出不可变快照；位置与用户设置不能借活引用绕过更新规则。 */
export type SettingsSnapshot = Readonly<Omit<Settings, 'widget' | 'metrics'>> & {
  readonly widget: Readonly<Settings['widget']>;
  readonly metrics: Readonly<Settings['metrics']>;
};

export interface SettingsHubOptions {
  initial: Settings;
  write(s: Settings): void;
  broadcast(s: Settings): void;
  effects: SettingsEffects & { applyCenteringRule(): void };
  env: SettingsEnv;
}

export interface SettingsHub {
  get(): SettingsSnapshot;
  /** 设置窗补丁：来源权限、合并、副作用、落盘与广播完整执行。 */
  applyPatch(patch: SettingsPatch): SettingsSnapshot;
  reconcileOnBoot(): void;
  /** 共享菜单专用，不允许设置窗补丁覆盖。 */
  setCenterInTaskbarRow(on: boolean): void;
  setTransparentDisplay(on: boolean): void;
  /** 位置即时记入内存但不广播，何时落盘由小窗 controller 决定。 */
  stagePosition(position: Settings['widget']): boolean;
  /** 写当前整份快照，避免防抖期间用户改设置被旧快照覆盖。 */
  flushPosition(): void;
}

function snapshot(s: Settings): SettingsSnapshot {
  return Object.freeze({
    ...s,
    widget: Object.freeze({ ...s.widget }),
    metrics: Object.freeze({ ...s.metrics })
  });
}

/** 设置更新 module：所有调用方只表达变更来源，不再自行选择 commit 或拼接生效顺序。 */
export function createSettingsHub(opts: SettingsHubOptions): SettingsHub {
  let current = snapshot(opts.initial);
  let positionPending = false;

  function persist(notify: boolean): void {
    opts.write(current);
    positionPending = false;
    if (notify) opts.broadcast(current);
  }

  return {
    get: () => current,
    applyPatch(patch) {
      current = snapshot(applySettingsPatch(current, patch, opts.effects, opts.env));
      persist(true);
      return current;
    },
    reconcileOnBoot() {
      const next = reconcileAutostartOnBoot(current, opts.effects, opts.env);
      if (next === current) return;
      current = snapshot(next);
      persist(false);
    },
    setCenterInTaskbarRow(on) {
      if (current.centerInTaskbarRow === on) return;
      current = snapshot({ ...current, centerInTaskbarRow: on });
      persist(true);
      // 必须先发布新真值，窗口求解才能读到刚开启的规则。
      opts.effects.applyCenteringRule();
    },
    setTransparentDisplay(on) {
      if (current.transparentDisplay === on) return;
      current = snapshot({ ...current, transparentDisplay: on });
      persist(true);
    },
    stagePosition(position) {
      const old = current.widget;
      if (old.x === position.x && old.y === position.y && old.docked === position.docked) return false;
      current = snapshot({ ...current, widget: position });
      positionPending = true;
      return true;
    },
    flushPosition() {
      if (positionPending) persist(false);
    }
  };
}
