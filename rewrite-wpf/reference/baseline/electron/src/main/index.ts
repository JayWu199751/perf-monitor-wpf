import { app, Menu, ipcMain, nativeTheme, Tray } from 'electron';
import type { BrowserWindow } from 'electron';
import { join } from 'node:path';
import type { Settings } from '../shared/types';
import { ELEVATED_FLAG, isElevated, relaunchElevated } from './elevate';
import { loadSettings, saveSettings } from './store';
import { createSettingsHub } from './settingsHub';
import type { SettingsEffects, SettingsPatch } from './settings';
import { createMetricsService, type MetricsOptions } from './metrics';
import { readCpuMem } from './sources/cpuMem';
import { readNetwork, warmNetwork } from './sources/network';
import { readGpu } from './sources/gpu';
import { disposeCpuTemp, readCpuTemp } from './sources/cpuTemp';
import { enableAutostart, disableAutostart, cleanupLegacyRunEntry } from './autostart';
import { createWidgetWindow } from './window/widget';
import { createSettingsWindow } from './window/settings';
import {
  createSettingsWindowController,
  SETTINGS_IDLE_DESTROY_MS,
  type SettingsWindowController
} from './settingsWindow';
import { buildAppMenu, createTray } from './tray';
import { createFullscreenWatcher } from './fullscreenWatcher';
import { getForegroundInfo } from './foreground';
import { isTokenElevated } from './win32';
import { createWidgetWindowController, type WidgetWindowController } from './widgetWindow';
import { createWidgetWindowPorts } from './widgetWindowPorts';

// 关掉 GPU 硬件加速：小窗是一行文字 + 圆角卡片，没有需要 GPU 合成的内容。
// 实测整棵 Electron 进程树私有内存 176.8MB -> 115.1MB（GPU 进程自身 77.7MB -> 15.6MB）。
// 渲染结果用 PrintWindow 逐像素比对过，与开硬件加速时一致；但**逐像素 alpha 通道尚未在
// 真实桌面上确认过**（PrintWindow 会把 alpha 拍平，而截屏取样要求桌面没被全屏应用盖住）。
// 若透明变成一块不透明底，删掉这一行即可，其余改动不受影响。必须在 app ready 之前设置。
app.commandLine.appendSwitch('disable-gpu');

let widget: WidgetWindowController | null = null;

let settingsWin: SettingsWindowController | null = null;
let metrics: ReturnType<typeof createMetricsService> | null = null;
// Tray 必须持有强引用：一旦被 GC，任务栏图标就消失而进程仍在
let tray: Tray | null = null;
let fullscreenWatcher: ReturnType<typeof createFullscreenWatcher> | null = null;
// 退出流程标志：before-quit 置位，设置窗 close 处理器据此放行（否则 app.quit()
// 被"关闭即隐藏"拦截中止，widget 关了进程还活着，托盘残留——见 ADR-0001）
let isQuitting = false;

function preloadPath(): string {
  return join(__dirname, '../preload/index.js');
}

function startMetrics(s: Settings = store.get()): void {
  metrics?.stop();
  const opts: MetricsOptions = {
    readCpuMem,
    readNetwork,
    readGpu,
    readCpuTemp,
    refreshFastMs: s.refreshFastMs,
    refreshSlowMs: s.refreshSlowMs,
    onSnapshot: (snap) => widget?.sendToRenderer('metrics', snap)
  };
  metrics = createMetricsService(opts);
  metrics.start();
}

function startFullscreenWatch(): void {
  if (fullscreenWatcher) return;
  fullscreenWatcher = createFullscreenWatcher({
    getForegroundInfo,
    onFullscreenChange: (full) => widget?.onFullscreenChange(full)
  });
  fullscreenWatcher.start();
}

function stopFullscreenWatch(): void {
  fullscreenWatcher?.stop();
  fullscreenWatcher = null;
}

// settings.ts 的策略在此落地：所有 electron/系统副作用集中注入
const settingsEffects: SettingsEffects = {
  setAutostart: (enable) => (enable ? enableAutostart() : disableAutostart()),
  cleanupLegacyAutostart: () => {
    cleanupLegacyRunEntry();
  },
  restartMetrics: (next) => startMetrics(next),
  // 三态主题直接映射 nativeTheme.themeSource（'system' 即跟随系统）：
  // 渲染层两个窗口用 @media (prefers-color-scheme) 自动换变量，无需 IPC；
  // 托盘图标经 nativeTheme.on('updated') 联动重选。
  applyTheme: (theme) => {
    nativeTheme.themeSource = theme;
  },
  setFullscreenWatch: (enable) => (enable ? startFullscreenWatch() : stopFullscreenWatch())
};

// 设置更新完整流程由 hub 拥有；组合根只映射持久化、广播和系统效果。
const store = createSettingsHub({
  initial: loadSettings(),
  write: saveSettings,
  broadcast: (s) => widget?.sendToRenderer('settings', s),
  effects: { ...settingsEffects, applyCenteringRule: () => widget?.applyCenteringRule() },
  env: { isPackaged: app.isPackaged }
});

// 一次性计时器同样只返回取消函数，结算与防抖规则留在 controller 内。
function schedule(callback: () => void, delayMs: number): () => void {
  const timer = setTimeout(callback, delayMs);
  timer.unref?.();
  return () => clearTimeout(timer);
}

// 周期定时器（z-order 守卫用）：交出去的是取消函数，所以 controller 侧不必知道 setInterval，
// 测试能在这一格拿到守卫的每一拍。间隔与被测的三条实测结论都在 widgetWindow.ts / zOrderGuard.ts。
function startTimer(tick: () => void, intervalMs: number): () => void {
  const timer = setInterval(tick, intervalMs);
  return () => clearInterval(timer);
}

function openSettings(): void {
  if (!settingsWin) {
    settingsWin = createSettingsWindowController({
      createWindow: () => createSettingsWindow(preloadPath()),
      isQuitting: () => isQuitting,
      idleDestroyMs: SETTINGS_IDLE_DESTROY_MS,
      schedule
    });
  }
  settingsWin.open();
}

function startApp(): void {
  if (!app.requestSingleInstanceLock()) {
    app.quit();
    return;
  }
  app.on('second-instance', () => {
    // 唤起已运行实例：走 controller 的手动显示入口，手动/自动隐藏标志随之清除
    widget?.reveal();
  });

  void app.whenReady().then(() => {
    Menu.setApplicationMenu(null);
    nativeTheme.themeSource = store.get().theme;

    // 自启与系统同步（仅打包环境）：先清 Run 键残留（setLoginItemSettings 时代遗留，
    // 是"开机自启失效"的直接根因），再把设置里的开关与计划任务实际状态对齐。
    store.reconcileOnBoot();

    void warmNetwork();

    // 应用级右键菜单只 build 一次：托盘与性能条右键共享同一实例
    const appMenu = buildAppMenu({
      toggleWidget: () => widget?.toggle(),
      openSettings,
      quit: () => app.quit(),
      isCenterInTaskbarRow: () => store.get().centerInTaskbarRow,
      setCenterInTaskbarRow: store.setCenterInTaskbarRow,
      isTransparentDisplay: () => store.get().transparentDisplay,
      setTransparentDisplay: store.setTransparentDisplay
    });

    widget = createWidgetWindowController({
      createWindow: () => createWidgetWindow(preloadPath()),
      // 贴边 / 任务栏遮挡 / 显示器事件三件真能力经端口注入：widgetWindow.ts 因此不 import
      // electron，小窗的全部窗口不变量都能在 vitest 里通过它的 interface 测
      ports: createWidgetWindowPorts,
      settings: store,
      metricsControls: {
        setPaused: (paused) => metrics?.setPaused(paused),
        setMoving: (moving) => metrics?.setMoving(moving)
      },
      startTimer,
      schedule,
      // 与托盘共享同一 Menu 实例：性能条右键 = 托盘右键
      popupMenu: (win) => appMenu.popup({ window: win as BrowserWindow })
    });

    if (store.get().autoHideOnFullscreen) startFullscreenWatch();

    startMetrics();

    tray = createTray({ menu: appMenu, onToggle: () => widget?.toggle() });

    ipcMain.handle('settings:get', () => store.get());

    ipcMain.handle('settings:set', (_e, patch: SettingsPatch) => store.applyPatch(patch));

    ipcMain.on('widget:resize', (_e, { width, height }: { width: number; height: number }) => {
      widget?.handleContentResize(width, height);
    });
  });

  // 常驻托盘：关掉所有窗口不退出，退出只经托盘菜单
  app.on('window-all-closed', () => {
    // no-op
  });

  app.on('before-quit', () => {
    // 先置位，设置窗 close 处理器放行，quit 才不会被"关闭即隐藏"中止
    isQuitting = true;
    metrics?.stop();
    disposeCpuTemp();
    stopFullscreenWatch();
    widget?.flushPosition();
    // 撤掉 z-order 守卫的定时器与显示器订阅（退出流程里 hide/closed 不一定发生）
    widget?.dispose();
    // 撤掉待触发的闲置销毁定时器，别让它在退出途中动窗口
    settingsWin?.dispose();
    // 显式摘掉图标：否则任务栏可能残留一个点了没反应的死图标
    tray?.destroy();
    tray = null;
  });
}

// 提权策略（防 UAC 循环）：
// - 打包 exe 已嵌入 requireAdministrator manifest，进程创建前由系统强制提升，
//   此时 isElevated() 恒为 true，不走本分支；
// - dev 模式跳过提权：提权重启会让 electron-vite 的 dev server 进程树随旧实例退出
//   （CLI 监听 electron close 即 exit），而 UAC 新进程不继承 ELECTRON_RENDERER_URL，
//   窗口回退加载 out/ 旧产物——开发界面永不更新。CPU 温度因此显示 `--`；
// - 若打包 exe 以普通权限启动（未嵌入 manifest 等）：非管理员且无 --elevated ->
//   UAC 提权重启一次；--elevated 由提权重启与自启计划任务携带，据此跳过本分支。
// 提权决策必须在单实例锁之前：提权后的新实例总能拿到锁，当前实例从不取锁。
if (app.isPackaged && !process.argv.includes(ELEVATED_FLAG) && !isElevated(isTokenElevated)) {
  relaunchElevated({
    onGranted: () => app.exit(0),
    // 用户拒绝 UAC -> 降级运行（CPU 温度显示 --，其余指标正常）
    onDenied: () => startApp()
  });
} else {
  startApp();
}
