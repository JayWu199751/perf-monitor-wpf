import { BrowserWindow } from 'electron';
import { join } from 'node:path';
import { WIDGET_CARD_HEIGHT } from '../../shared/types';
import { appIconPath } from './icon';

// 纯窗口工厂：配置 + 加载页面。这里的高只是**首帧初值**：真实卡高由渲染端按最大字样的
// 墨迹量出后经 resizeWidget 上报（ADR-0006 修正三）。Windows 高 DPI(本机 175%)会把透明
// 无边框窗钳到 ~38px 最小，比卡片高也没关系——卡片在窗口内居中，贴边推出吃的是上报值。
// 位置/可见性等不变量由 widgetWindow.ts 的 controller 负责。
export function createWidgetWindow(preloadPath: string): BrowserWindow {
  const win = new BrowserWindow({
    icon: appIconPath(),
    width: 360,
    height: WIDGET_CARD_HEIGHT,
    // 尺寸以内容区为准，跨 DPI 时外框/内容换算一致；落位由 controller 回读实际窗口高
    useContentSize: true,
    frame: false,
    transparent: true,
    alwaysOnTop: true,
    skipTaskbar: true,
    // resizable:false 顺带屏蔽 Windows 的贴边分屏（拖到边不弹 Snap Assist 布局）
    resizable: false,
    fullscreenable: false,
    maximizable: false,
    minimizable: false,
    hasShadow: false,
    show: false,
    webPreferences: {
      preload: preloadPath,
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  });

  if (process.env['ELECTRON_RENDERER_URL']) {
    void win.loadURL(`${process.env['ELECTRON_RENDERER_URL']}/widget.html`);
  } else {
    void win.loadFile(join(__dirname, '../renderer/widget.html'));
  }

  return win;
}
