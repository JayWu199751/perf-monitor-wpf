import { screen } from 'electron';
import { isSystemWindowClass, type Edges } from './fullscreen';
import { getForegroundWindow } from './win32';

export interface ForegroundInfo {
  rect: Edges;
  // 显示器 bounds（DIP 单位）
  bounds: Edges;
  scaleFactor: number;
  style: number;
}

// 前台窗口 → 全屏判定输入：
// 1) win32 层拿原始 rect + 类名，排除桌面/任务栏等系统窗口；
// 2) GetWindowRect 的坐标单位随目标窗口 DPI 状态漂移（物理像素或 DIP），
//    因此 bounds 保留 DIP 原值并附上 scaleFactor，由判定侧两种单位都试。
export function getForegroundInfo(): ForegroundInfo | null {
  const fg = getForegroundWindow();
  if (!fg || isSystemWindowClass(fg.className)) return null;
  const dip = screen.screenToDipPoint({ x: fg.rect.left, y: fg.rect.top });
  const display = screen.getDisplayNearestPoint(dip);
  const b = display.bounds;
  const bounds: Edges = {
    left: b.x,
    top: b.y,
    right: b.x + b.width,
    bottom: b.y + b.height
  };
  return { rect: fg.rect, bounds, scaleFactor: display.scaleFactor, style: fg.style };
}
