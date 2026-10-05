import { cardCenterY } from './geometry';
import type { ContentSize, Rect } from './geometry';
import type { DockEdge, DockMask } from '../shared/types';
import { WIDGET_CARD_HEIGHT } from '../shared/types';

// 贴边悬靠的纯几何层：只吃矩形、只吐矩形，不碰任何窗口 API，因此可整体单测。
// 落位与结算时序在 widgetWindow.ts，真实矩形与摆窗能力经 widgetWindowPorts.ts 注入。

export const DOCK_THRESHOLD = 8;
// 设计上的卡片四周透明安全边距（DIP）：窗口比卡片每边多出的透明区。
// 贴哪条边就把这条边连同透明区推出屏幕，使可视卡片正好 1px 贴边（ADR-0003）。
export const EDGE_MARGIN = 6;
export const DOCK_INSET = 1; // 底部保留 1px 避免描边被工作区边缘裁掉

// 卡片尺寸是纯几何词汇，住在 geometry.ts；这里原样转出去，调用方沿用 dock.ts 这条路径。
export type { ContentSize } from './geometry';

const EDGE_ORDER: DockEdge[] = ['left', 'right', 'top', 'bottom'];
// 掩码归一化顺序：top/bottom 在前，保证 'top-left' 而非 'left-top'
const MASK_ORDER: DockEdge[] = ['top', 'bottom', 'left', 'right'];

// 返回所有与工作区边缘距离 <= 阈值的贴边边（可多条，如同时贴左上角）。
export function nearestEdges(bounds: Rect, wa: Rect): DockEdge[] {
  const dist: Record<DockEdge, number> = {
    left: Math.max(0, bounds.x - wa.x),
    right: Math.max(0, wa.x + wa.width - (bounds.x + bounds.width)),
    top: Math.max(0, bounds.y - wa.y),
    bottom: Math.max(0, wa.y + wa.height - (bounds.y + bounds.height))
  };
  return EDGE_ORDER.filter((edge) => dist[edge] <= DOCK_THRESHOLD);
}

export function edgesToMask(edges: DockEdge[]): DockMask | null {
  const present = MASK_ORDER.filter((edge) => edges.includes(edge));
  if (present.length === 0) return null;
  return present.join('-') as DockMask;
}

export function maskToEdges(mask: DockMask | null): DockEdge[] {
  if (!mask) return [];
  return mask.split('-') as DockEdge[];
}

// 窗口比卡片每边多出的透明边距（DIP）。依赖最后一次 resize 上报的卡片尺寸；
// 卡片尺寸未知时按 WIDGET_CARD_HEIGHT 这个兜底初值估算——卡高现在随字号由「最大字样的墨迹」
// 派生，真值只有渲染端量得出来（见 shared/types.ts 的注释），主进程只能等上报。
// 这里不兜底就会踩 Windows 高 DPI 下窗口被钳到 38px 时
// 按窗口高度估算导致的垂直推出误差（启动即偏移，resize 再漂移）。
export function margins(bounds: Rect, content: ContentSize | null): { h: number; v: number } {
  const cw = content && content.width > 0 ? content.width : bounds.width - 2 * EDGE_MARGIN;
  const ch = content && content.height > 0 ? content.height : WIDGET_CARD_HEIGHT;
  return {
    h: Math.max(0, Math.round((bounds.width - cw) / 2)),
    v: Math.max(0, Math.round((bounds.height - ch) / 2))
  };
}

/**
 * 卡片尺寸 → **要请求给 `setBounds` 的窗口尺寸**。宽度是卡片 + 每边 `EDGE_MARGIN` 的透明边距，
 * 高度就是卡片高（多出来的那截由 OS 的最小高度钳制决定，不归这里管）。
 *
 * **钳制后的真尺寸不在这里预测**（2026-09 修正）：原先本函数还返回 `actual = max(request, 窗口现高)`，
 * 让求解吃到"预测的 OS 最小高度"。真机量下来这条预测会差 1~4 DIP（创建期窗口 41 DIP，落位后 37 DIP），
 * 而它错多少、垂直居中就偏一半——实测任务栏行内上下留空 7.74/11.26 物理px（差 3.52）。
 * 现在改为**落尺寸之后回读**：`widgetWindow.placeWidget` 先 `setBounds` 尺寸、再 `getBounds` 问窗口，
 * 用真值求解落点。量具与全部读数在 `scripts/dock/probe-row-center.cjs`，判断见 ADR-0005 修正三。
 *
 * `content = null`（渲染端还没上报）时保持现有尺寸，与旧的 'redock' 语义一致。
 */
export function windowSizingFor(bounds: Rect, content: ContentSize | null): ContentSize {
  // 透明无边框窗的最小高度由 Chromium 写死（本机 ~37 DIP），请求更矮也不会兑现；
  // 兑现的那个数只有窗口自己知道，所以这里只答"请求什么"，不答"会变成什么"。
  return content
    ? { width: content.width + 2 * EDGE_MARGIN, height: content.height }
    : { width: bounds.width, height: bounds.height };
}

// 显示器的两个矩形。字段与 Electron.Display 的 bounds / workArea 同形，因此结构上可直接互传，
// 但本模块不 import electron —— 「任务栏行」这条几何规则属于纯几何层，窗口侧只负责报来它落在哪块屏。
export interface DisplayRects {
  bounds: Rect;
  workArea: Rect;
}

// 卡片中心是否落在任务栏行内——"要进行内"与"贴行下"的唯一判据。
// 取中心而非窗口上沿：贴边后透明边距被推出屏幕，上沿坐标为负，不表达用户意图。
export function cardCenterInRow(bounds: Rect, row: Rect | null): boolean {
  if (row == null || row.height <= 0) return false;
  const cy = cardCenterY(bounds);
  return cy >= row.y && cy < row.y + row.height;
}

// 任务栏行矩形：显示器边界与工作区边界之间那条水平带。Electron 的 workArea 已经是
// "排除任务栏"之后的区域，两者之差就是任务栏占位，不需要 FFI 去问 ABM_GETTASKBARPOS。
// 左右侧任务栏时那条带是竖直的，本形态不支持行内，返回 null。
// 上下同时有占位带（第三方 appbar + 任务栏）时按卡片中心落在哪条带来选，避免误吸到不是任务栏的那条。
// 两条带都不含中心时仍返回"存在的那条"而不是 null：调用方 cardCenterInRow 会把它判掉，
// 于是"这里其实有一条 appbar 带"这件事保留在读数里，行内与贴行下走的仍是同一条求解路径。
export function taskbarRow(display: DisplayRects, bounds: Rect): Rect | null {
  const b = display.bounds;
  const wa = display.workArea;
  if (wa.x !== b.x || wa.width !== b.width) return null;
  const topGap = wa.y - b.y;
  const bottomGap = b.y + b.height - (wa.y + wa.height);
  const top: Rect = { x: b.x, y: b.y, width: b.width, height: topGap };
  const bottom: Rect = { x: b.x, y: wa.y + wa.height, width: b.width, height: bottomGap };
  const cy = cardCenterY(bounds);
  if (topGap > 0 && cy >= top.y && cy < top.y + top.height) return top;
  if (bottomGap > 0 && cy >= bottom.y && cy < bottom.y + bottom.height) return bottom;
  return topGap > 0 ? top : bottomGap > 0 ? bottom : null;
}

// 小窗此刻是否停在**任务栏行内**（CONTEXT.md 的术语）：行矩形存在 **且** 卡片中心落在其中。
// 「进行后只重算纵向」与「要不要开 z-order 守卫」都吃这一条判据（ADR-0005），
// 所以它必须与行矩形住在同一个 module 里，否则两边可能各自漂移。
export function cardInTaskbarRow(display: DisplayRects, bounds: Rect): boolean {
  return cardCenterInRow(bounds, taskbarRow(display, bounds));
}

// 要不要重新申请 z-order 档位的判据（纯函数，FFI 侧只提供两个布尔输入）。
// 只在"停在任务栏行内"且"卡片中心已被任务栏占住"时动作：
// 悬浮态被别的窗口盖住是正常的 z-order，不该去抢；而在行内被任务栏盖住等于看不见。
export function needsZReassert(inTaskbarRow: boolean, coveredByTaskbar: boolean): boolean {
  return inTaskbarRow && coveredByTaskbar;
}

// 行可用 = 给了行矩形、有高度、且卡片中心落在其中。顶栏/底栏共用这一条判据。
function usableRow(bounds: Rect, row: Rect | null | undefined): Rect | null {
  return cardCenterInRow(bounds, row ?? null) ? (row as Rect) : null;
}

// 卡片在客户区内垂直居中（`#app` 是 flex + align-items:center），所以把窗口居中到行就等于把卡片
// 居中到行——这条前提是**量过的**，不是推的：`scripts/dock/probe-row-center.cjs` 在真 DPI 下用
// capturePage 的 alpha 边界量卡片矩形，实测卡片中心离客户区中心 ≤0.9 物理px（那 0.9px 是
// Chromium 给「小数高卡片」吸附像素网格的固有下限，随字号从 0.19 到 0.55 CSS px 不等）。
//
// 推论有两条，都被量具钉住：
// ① `bounds` 必须是 **OS 真给的那个矩形**（落尺寸之后回读的值）。用预测值算，误差的一半会直接
//    变成偏心：实测预测 41 vs 真值 37 时上下留空 7.74/11.26 物理px。
// ② 这里只能取整 DIP —— Electron 的 setPosition/setBounds 收到小数会抛 conversion failure（实测），
//    所以余下 ≤0.5 DIP（≤0.9 物理px）的取整误差是这条路的下限，不再追。
function rowCenteredY(bounds: Rect, row: Rect): number {
  return Math.round(row.y + (row.height - bounds.height) / 2);
}

// 行内居中的横向落点：卡片同样在窗口内水平居中，居中窗口即居中卡片。
// 参照就是行矩形本身（= 显示器全宽），不做任何托盘/中部图标避让——
// 与 ADR-0005 否决"托盘对齐"不冲突：避让要查任务栏子控件几何，居中不需要。
function rowCenteredX(bounds: Rect, row: Rect): number {
  return Math.round(row.x + (row.width - bounds.width) / 2);
}

/**
 * 计算贴边后的窗口位置：贴中的边把透明区推出屏幕（x/y 向屏幕外偏移），
 * 卡片（居中）正好贴住该边；未贴的轴保持原位置并夹在工作区内。
 *
 * 输入是一个具名记录，因为这道题的五个量此前以六个位置参数排出，第三参与第五参都是四个数字的
 * 矩形，调用点随时能悄悄互换。`display` 直接吃显示器的两个矩形——**任务栏行**由它现场派生
 * （`taskbarRow`），行矩形不再是调用方要自备的输入；「求解用的矩形」与「显示器」谁配谁也就没有
 * 第二次回答的机会（ADR-0005 的两条硬约束收进 interface 本身）。
 */
export interface DockInput {
  /** 求解用的窗口矩形（DIP）。**必须是 OS 真给的那个**：resize 路径先落尺寸再回读，其余传 getBounds 的实时值 */
  bounds: Rect;
  /** 窗口中心所在显示器的两个矩形；行内落点由它派生，不需要调用方预先算好 */
  display: DisplayRects;
  mask: DockMask | null;
  /** 渲染端上报的卡片尺寸，null = 尚未上报，按 WIDGET_CARD_HEIGHT 兜底 */
  content: ContentSize | null;
  /** 设置「行内居中」（菜单文案"任务栏内水平居中"）的当前值：勾选且在行内时横向由规则独占 */
  centerInRow?: boolean;
}

export function dockedPosition(input: DockInput): { x: number; y: number } {
  const { bounds, mask, content, display, centerInRow = false } = input;
  const wa = display.workArea;
  // 行矩形是显示器的派生量，不是输入：调用方拿不到"自备一条行"的机会。
  const row = taskbarRow(display, bounds);
  const edges = maskToEdges(mask);
  const m = margins(bounds, content);
  let x = bounds.x;
  let y = bounds.y;
  if (edges.includes('left')) x = wa.x - m.h;
  if (edges.includes('right')) x = wa.x + wa.width - bounds.width + m.h;
  // 贴顶/贴底时先看能不能进任务栏行；进不了才是原来的"贴工作区边缘"。
  const inRow = usableRow(bounds, row);
  if (edges.includes('top')) y = inRow ? rowCenteredY(bounds, inRow) : wa.y - m.v;
  if (edges.includes('bottom')) {
    y = inRow
      ? rowCenteredY(bounds, inRow)
      : wa.y + wa.height - bounds.height + m.v - DOCK_INSET;
  }
  // 行内居中依附于"进内"落点：未贴顶/底、无行矩形、中心在行外时一概不生效。
  if (centerInRow && inRow && (edges.includes('top') || edges.includes('bottom'))) {
    x = rowCenteredX(bounds, inRow);
  } else if (!edges.includes('left') && !edges.includes('right')) {
    // 未贴的轴夹回工作区，避免被 OS 钳制或旧位置带出屏幕。
    x = Math.max(wa.x, Math.min(x, wa.x + wa.width - bounds.width));
  }
  if (!edges.includes('top') && !edges.includes('bottom')) {
    y = Math.max(wa.y, Math.min(y, wa.y + wa.height - bounds.height));
  }
  return { x: Math.round(x), y: Math.round(y) };
}
