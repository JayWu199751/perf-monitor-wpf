import { BrowserWindow, screen } from 'electron';
import { join } from 'node:path';
import { appIconPath } from './icon';
import { settingsWindowHeight } from '../settingsWindow';

// 纯窗口工厂：只管配置 + 加载页面。关闭即隐藏、闲置销毁、退出放行这些不变量
// 归 settingsWindow.ts 的 controller，这样策略不 import electron、可单测。
export function createSettingsWindow(preloadPath: string): BrowserWindow {
  const win = new BrowserWindow({
    icon: appIconPath(),
    width: 380,
    height: settingsWindowHeight(screen.getPrimaryDisplay().workAreaSize.height),
    useContentSize: true,
    resizable: false,
    title: '性能小窗 · 设置',
    autoHideMenuBar: true,
    show: false,
    webPreferences: {
      preload: preloadPath,
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  });

  // 首次 show 前需等页面就绪，否则用户看到空白窗体
  win.once('ready-to-show', () => win.show());

  if (process.env['ELECTRON_RENDERER_URL']) {
    void win.loadURL(`${process.env['ELECTRON_RENDERER_URL']}/settings.html`);
  } else {
    void win.loadFile(join(__dirname, '../renderer/settings.html'));
  }

  return win;
}
