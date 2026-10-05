// 屏幕矩形。字段与 Electron.Rectangle 同形，可直接互传，但本模块不 import electron，
// 使纯几何逻辑（dock.ts）能在 vitest 里脱离 Electron 运行时执行。
export interface Rect {
  x: number;
  y: number;
  width: number;
  height: number;
}

// 显示器边界用 left/top/right/bottom 表示：全屏判定要逐边比对，
// 与 GetWindowRect 输出的 RECT 结构一致（物理像素）。
export interface Edges {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

// 卡片尺寸（DIP），不含四周透明边距。它住在纯几何词汇里而不是 dock.ts：
// 上报方（渲染端）、求解方（dock.ts）、小窗 controller（widgetWindow.ts） 都要认它，
// 而 controller 刻意不直连 dock.ts —— 类型留在 geometry.ts，那条接缝才不漏。
export interface ContentSize {
  width: number;
  height: number;
}

// 卡片中心的 y（DIP）。判定"要不要进任务栏行"以及按中心解析显示器都用它，
// 不用窗口上沿：贴边后透明边距被推出屏幕，上沿坐标为负，不表达用户意图。
export function cardCenterY(bounds: Rect): number {
  return bounds.y + bounds.height / 2;
}
