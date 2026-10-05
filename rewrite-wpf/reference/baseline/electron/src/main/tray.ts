import { Menu, Tray, nativeImage, nativeTheme, screen } from 'electron';
import type { NativeImage } from 'electron';
import { resourcePath } from './paths';

export interface TrayOptions {
  // 应用级右键菜单：托盘与性能条右键共享同一 Menu 实例（index.ts build 一次）
  menu: Menu;
  onToggle: () => void;
}

// 菜单模板的动作入口（性能条与托盘共用同一份动作）
export interface TrayHandlers {
  toggleWidget: () => void;
  openSettings: () => void;
  quit: () => void;
  // 行内居中（ADR-0005 后续修正二）：读当前值供 build 时刻初始 checked，也供 click 时取反。
  // 写唯一入口是本菜单项。type:'checkbox' 由 Electron 在点击时自动翻转 checked；
  // 处理函数刻意不读 item.checked（翻转与 click 的先后不作前提），
  // 以 settings 为准取反——build 时一致 + 每击同步，则勾选态与设置永不漂移。
  isCenterInTaskbarRow(): boolean;
  setCenterInTaskbarRow(on: boolean): void;
  isTransparentDisplay(): boolean;
  setTransparentDisplay(on: boolean): void;
}

// Electron 在 Windows 上把托盘图转成 HICON 时只取 NativeImage 的 1x 表示
// （Tray::SetImage → NativeImage::GetHICON(SM_CXSMICON) → CreateHICONFromSkBitmap(AsBitmap())），
// @1.25x~@2x 阶梯不会被选用：16px 基图由系统拉伸到 28px(175%) 造成模糊。
// 因此按主屏缩放比直接加载"恰好目标物理尺寸"的单一表示，HICON 1:1 无重采样。
const TRAY_SIZES = [16, 20, 24, 28, 32];

function trayIconImage(): NativeImage {
  // 浅色主题用深色线、深色主题用浅色线，保证在任务栏始终可见
  const theme = nativeTheme.shouldUseDarkColors ? 'tray-dark' : 'tray-light';
  const target = Math.round(16 * screen.getPrimaryDisplay().scaleFactor);
  const sizes = [...TRAY_SIZES].sort((a, b) => Math.abs(a - target) - Math.abs(b - target));
  for (const size of sizes) {
    const img = nativeImage.createFromPath(resourcePath(`${theme}-${size}.png`));
    if (!img.isEmpty()) return img;
  }
  return nativeImage.createEmpty();
}

// 应用级右键菜单模板：显示/隐藏小窗 / 打开设置 / 透明显示与行内居中开关 / 退出。
// 调用方（index.ts）只 build 一次，托盘与性能条右键共用同一 Menu 实例。
// 「任务栏内水平居中」是持久化的行为规则而非一次性动作：菜单即唯一入口，设置窗不露出。
export function buildAppMenu(handlers: TrayHandlers): Electron.Menu {
  return Menu.buildFromTemplate([
    { label: '显示/隐藏小窗', click: () => handlers.toggleWidget() },
    { label: '打开设置', click: () => handlers.openSettings() },
    {
      label: '任务栏内水平居中',
      type: 'checkbox',
      checked: handlers.isCenterInTaskbarRow(),
      click: () => handlers.setCenterInTaskbarRow(!handlers.isCenterInTaskbarRow())
    },
    {
      label: '透明显示',
      type: 'checkbox',
      checked: handlers.isTransparentDisplay(),
      click: () => handlers.setTransparentDisplay(!handlers.isTransparentDisplay())
    },
    { type: 'separator' },
    { label: '退出', click: () => handlers.quit() }
  ]);
}

export function createTray(opts: TrayOptions): Tray {
  const tray = new Tray(trayIconImage());
  tray.setToolTip('性能小窗');
  tray.setContextMenu(opts.menu);
  // 左键单击切换小窗显隐；右键走 setContextMenu 挂的菜单
  tray.on('click', () => opts.onToggle());
  // 主题（含 theme=system 下系统明暗切换）与 DPI 变更时重选图标
  nativeTheme.on('updated', () => tray.setImage(trayIconImage()));
  try {
    screen.on('display-metrics-changed', () => tray.setImage(trayIconImage()));
  } catch {
    // 无显示器时 screen 事件不可用，忽略
  }
  return tray;
}
