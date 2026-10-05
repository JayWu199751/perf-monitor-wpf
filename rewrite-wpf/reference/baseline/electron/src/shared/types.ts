export type DockEdge = 'left' | 'right' | 'top' | 'bottom';

// 贴边掩码：可同时贴多条边（如左上角 'top-left'）。由 edgesToMask 生成，顺序固定为 top/bottom 在前。
export type DockMask =
  | 'left'
  | 'right'
  | 'top'
  | 'bottom'
  | 'top-left'
  | 'top-right'
  | 'bottom-left'
  | 'bottom-right';

export type Theme = 'dark' | 'light' | 'system';

// 最大字样到卡片**可视边框**的目标距离（CSS px）：卡片高度不再是个常数，
// 而是「跟着字号呼吸」——渲染端把卡片裁到最大字号的墨迹外框，再上下各留这一个数，
// 于是字号 10→18 时这一格距离恒定（旧版把卡高钉成 22px，同一格实测从 6.6px 掉到 3.4px）。
// 改这一个数就够了：渲染层的 --edge-gap 与量具的边距目标都从它取，改完跑一次
// npm run measure:alignment 复验（卡高会整体长高 2×Δ，贴边与任务栏行内都吃上报值，无需另改）。
// 参照是「最大的字样」= 字号等于 --fs 的那批 run（主读数与时钟）的**墨迹**上下沿，
// 判据在 npm run measure:alignment 的「边距」列，理由与实现见 ADR-0006 后续修正三。
export const WIDGET_EDGE_GAP = 6;

// 卡高的**初值与兜底**（DIP）：窗口工厂建窗时、以及渲染端尚未上报卡片尺寸时用它估
// 贴边推出偏移。真正的卡高由渲染端量出并经 resizeWidget 上报（见 widgetWindow.handleContentResize），
// 所以下面这个数不需要精确，只需要离真实值别太远（默认字号 12 下实测 16.8）。
// 它不再是「主/渲染同源的几何真值」——那条旧等式（--lh-widget == 本常量 − 2）随
// 卡片高度改为墨迹派生一起作废，改由 tests/opticalCenter.test.ts 钉住新的形状约束。
export const WIDGET_CARD_HEIGHT = 17;

export interface MetricsSnapshot {
  cpuPct: number | null;
  cpuTemp: number | null;
  memUsedGb: number | null;
  memTotalGb: number;
  memPct: number | null;
  gpuPct: number | null;
  gpuMemPct: number | null;
  gpuTemp: number | null;
  netDownMBs: number | null;
  netUpMBs: number | null;
  ts: number;
}

export interface Settings {
  widget: { x: number; y: number; docked: DockMask | null };
  metrics: { cpu: boolean; mem: boolean; gpu: boolean; net: boolean; time: boolean };
  refreshFastMs: number;
  refreshSlowMs: number;
  autostart: boolean;
  autoHideOnFullscreen: boolean;
  // 行内居中：小窗停在任务栏行内时，横向位置由规则独占——水平居中到行矩形。
  // 唯一入口是共享应用菜单的「任务栏内水平居中」checkbox，设置窗不露出（ADR-0005 后续修正二）。
  centerInTaskbarRow: boolean;
  // 透明显示：共享右键菜单的开关；开启时保留指标文字、隐藏非文字视觉元素。
  transparentDisplay: boolean;
  opacity: number;
  fontSize: number;
  theme: Theme;
}

// preload 经 contextBridge 暴露给渲染层的唯一门面（Vue 组件只认这个接口）
export interface PerfApi {
  onMetrics(cb: (s: MetricsSnapshot) => void): () => void;
  onSettings(cb: (s: Settings) => void): () => void;
  getSettings(): Promise<Settings>;
  setSettings(patch: Partial<Settings>): Promise<Settings>;
  resizeWidget(width: number, height: number): void;
}
