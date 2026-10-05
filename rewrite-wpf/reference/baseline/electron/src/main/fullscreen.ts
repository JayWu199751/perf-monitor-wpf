import type { Edges } from './geometry';

// 判定层的输入类型随判定函数一起对外提供，调用方不必直接依赖 geometry.ts
export type { Edges };

// WS_CAPTION（标题栏）/ WS_THICKFRAME（可缩放边框）：普通窗口的标志。
// 最大化 ≠ 全屏，但排除依据是"带窗口框"而非 WS_MAXIMIZE——
// Chrome F11 全屏窗口实测带 WS_MAXIMIZE 却无标题栏边框，按 MAXIMIZE 排除会漏掉它。
export const WS_CAPTION = 0x00c00000;
export const WS_THICKFRAME = 0x00040000;

// 全屏窗口 rect 与显示器边界的允许误差：Chrome F11 全屏底部实测差 1px。
const FULLSCREEN_TOLERANCE = 2;

// GetWindowRect 返回的单位随目标窗口的 DPI 状态漂移（实测 Chrome 两种全屏
// 进入方式分别返回物理像素与 DIP），因此与显示器边界比较时两种单位都试。
export function isWindowFullscreen(
  rect: Edges,
  bounds: Edges,
  scaleFactor: number,
  style: number = 0
): boolean {
  if ((style & WS_CAPTION) !== 0 || (style & WS_THICKFRAME) !== 0) return false;
  const physicalBounds: Edges = {
    left: bounds.left * scaleFactor,
    top: bounds.top * scaleFactor,
    right: bounds.right * scaleFactor,
    bottom: bounds.bottom * scaleFactor
  };
  return matchesBounds(rect, bounds) || matchesBounds(rect, physicalBounds);
}

function matchesBounds(a: Edges, b: Edges): boolean {
  return (
    Math.abs(a.left - b.left) <= FULLSCREEN_TOLERANCE &&
    Math.abs(a.top - b.top) <= FULLSCREEN_TOLERANCE &&
    Math.abs(a.right - b.right) <= FULLSCREEN_TOLERANCE &&
    Math.abs(a.bottom - b.bottom) <= FULLSCREEN_TOLERANCE
  );
}

// 桌面（Progman/WorkerW）与任务栏（Shell_TrayWnd）窗口的 rect 恰好铺满屏幕，
// 点击桌面空白处会让前台窗口变成它们——排除掉，避免误判全屏。
const SYSTEM_WINDOW_CLASSES = new Set(['Progman', 'WorkerW', 'Shell_TrayWnd']);

export function isSystemWindowClass(className: string): boolean {
  return SYSTEM_WINDOW_CLASSES.has(className);
}
