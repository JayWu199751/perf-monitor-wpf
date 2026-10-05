import type { Settings, Theme } from '../shared/types';

export const DEFAULT_SETTINGS: Settings = {
  widget: { x: 0, y: 0, docked: null },
  metrics: { cpu: true, mem: true, gpu: true, net: true, time: true },
  refreshFastMs: 1000,
  refreshSlowMs: 3000,
  autostart: false,
  autoHideOnFullscreen: true,
  centerInTaskbarRow: false,
  transparentDisplay: false,
  opacity: 0.72,
  fontSize: 12,
  theme: 'system'
};

// 设置模块不 import electron：副作用全部经 SettingsEffects 注入（组合根执行），
// 本模块只拥有"何时/以什么参数/结果如何写回 settings"的策略，可整体单测。
export interface SettingsEffects {
  // 返回计划任务操作是否成功——结果写回 settings.autostart
  setAutostart(enable: boolean): boolean;
  cleanupLegacyAutostart(): void;
  restartMetrics(next: Settings): void;
  applyTheme(theme: Theme): void;
  setFullscreenWatch(enable: boolean): void;
}

export interface SettingsEnv {
  isPackaged: boolean;
}

// 磁盘 JSON 可能缺键甚至缺整个子对象（手改配置文件是合法用法）
export type ParsedSettings = Partial<Omit<Settings, 'widget' | 'metrics'>> & {
  widget?: Partial<Settings['widget']>;
  metrics?: Partial<Settings['metrics']>;
};

// 磁盘上的部分配置补全为完整 Settings：widget/metrics 子对象逐键补默认值。
// 与 applySettingsPatch 的 merge 语义刻意不同：load 要保留磁盘上的 widget 位置。
export function fillDefaultSettings(
  parsed: ParsedSettings,
  defaults: Settings = DEFAULT_SETTINGS
): Settings {
  return {
    ...defaults,
    ...parsed,
    widget: { ...defaults.widget, ...(parsed.widget ?? {}) },
    metrics: { ...defaults.metrics, ...(parsed.metrics ?? {}) }
  };
}

// 渲染端/IPC 可发送的补丁形状：metrics 子对象可缺键（深合并补齐）。
// widget 补丁会被策略整体丢弃——小窗位置/贴边由主进程独占，见 applySettingsPatch。
// centerInTaskbarRow / transparentDisplay 类型上就不可发：唯一入口是共享菜单。
export type SettingsPatch = Partial<
  Omit<Settings, 'widget' | 'metrics' | 'centerInTaskbarRow' | 'transparentDisplay'>
> & {
  widget?: Partial<Settings['widget']>;
  metrics?: Partial<Settings['metrics']>;
};

// IPC settings:set 补丁的完整语义（策略的唯一 owner）：
// 1. metrics 子对象深合并——渲染端只发改动的键；
// 2. widget 整体丢弃——小窗位置/贴边由主进程独占（controller 经 settingsHub.stagePosition 写入）；
// 3. centerInTaskbarRow / transparentDisplay 整体丢弃——两者的唯一入口都是共享菜单，
//    settingsHub 的菜单专用入口处理；放行 IPC 会造成 checkbox 与设置值漂移；
// 4. 其余顶层键浅合并（未知键会随之落盘，自用工具接受无 schema 校验）。
// 副作用按序触发：改刷新间隔 → 重建采样服务（传入合并后的 next，避免读到旧值）；
// 自启写回规则：dev 不写系统自启保持 false；计划任务创建成功才视为开启，失败回落 false。
export function applySettingsPatch(
  current: Settings,
  patch: SettingsPatch,
  effects: SettingsEffects,
  env: SettingsEnv
): Settings {
  const next: Settings = {
    ...current,
    ...patch,
    widget: current.widget,
    centerInTaskbarRow: current.centerInTaskbarRow,
    transparentDisplay: current.transparentDisplay,
    metrics: { ...current.metrics, ...(patch.metrics ?? {}) }
  };
  if (patch.refreshFastMs || patch.refreshSlowMs) {
    effects.restartMetrics(next);
  }
  if (patch.theme) {
    effects.applyTheme(patch.theme);
  }
  if (typeof patch.autostart === 'boolean') {
    if (!env.isPackaged) {
      next.autostart = false;
    } else if (patch.autostart) {
      next.autostart = effects.setAutostart(true);
    } else {
      effects.setAutostart(false);
      next.autostart = false;
    }
  }
  if (typeof patch.autoHideOnFullscreen === 'boolean') {
    effects.setFullscreenWatch(patch.autoHideOnFullscreen);
  }
  return next;
}

// 启动时把自启状态与系统对齐（仅打包环境）：先清 setLoginItemSettings 时代遗留的
// Run 键自启项；开着但计划任务创建失败 → 返回写回 false 的副本，避免"设置显示开、
// 系统没任务"的撒谎状态。未纠正时返回原引用，调用方据引用判断是否需要落盘。
export function reconcileAutostartOnBoot(
  settings: Settings,
  effects: SettingsEffects,
  env: SettingsEnv
): Settings {
  if (!env.isPackaged) return settings;
  effects.cleanupLegacyAutostart();
  if (settings.autostart) {
    if (!effects.setAutostart(true)) {
      return { ...settings, autostart: false };
    }
  } else {
    effects.setAutostart(false);
  }
  return settings;
}
