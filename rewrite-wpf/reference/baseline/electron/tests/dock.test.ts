import { describe, expect, it } from 'vitest';
import {
  DOCK_INSET,
  DOCK_THRESHOLD,
  EDGE_MARGIN,
  cardInTaskbarRow,
  dockedPosition,
  edgesToMask,
  margins,
  maskToEdges,
  nearestEdges,
  needsZReassert,
  taskbarRow,
  windowSizingFor,
  type ContentSize,
  type DisplayRects
} from '../src/main/dock';
import type { Rect } from '../src/main/geometry';
import { WIDGET_CARD_HEIGHT, type DockMask } from '../src/shared/types';

// 1920x1040 工作区（底部 40px 是任务栏），窗口 = 卡片 360×26 + 每边 EDGE_MARGIN。
// CARD 是「场景内的卡高」，dock.ts 的几何全按传进来的 content 算，与真实常量无关，
// 所以这里保持局部值；唯一必须跟常量同源的是兜底那条（margins 用 WIDGET_CARD_HEIGHT），
// 见下面 describe('margins') 里的断言——它引常量而不是引本文件的 26。
const WA: Rect = { x: 0, y: 0, width: 1920, height: 1040 };
const CARD: ContentSize = { width: 360, height: 26 };
const WIN_W = 360 + 2 * EDGE_MARGIN;
// 高度受 OS 最小尺寸钳制，比卡片多出 ~12px（ADR-0003）
const WIN_H = 38;

const bounds = (x: number, y: number): Rect => ({ x, y, width: WIN_W, height: WIN_H });

// —— 显示器簇 ——
// dockedPosition 吃 DockInput：位置由「窗口矩形 + 所在显示器的两个矩形」决定，**任务栏行**由显示器
// 现场派生（taskbarRow = bounds − workArea），不再是调用方自备的第 5 个参数。所以下面每道题都得先
// 答「这是哪块屏」——而这块屏只能从真机的形状里挑，不能为了让某条断言成立而拼。
// 合成屏全高 1080、行高 40；底部那块的 workArea 就是上面那个 WA。
const FULL: Rect = { x: 0, y: 0, width: 1920, height: 1080 };
/** 任务栏在顶：行 (0,0)-(1920,40)，工作区从 y=40 起 */
const WA_ROW: Rect = { x: 0, y: 40, width: 1920, height: 1040 };
const ROW: Rect = { x: 0, y: 0, width: 1920, height: 40 };
/** 任务栏在底：行 (0,1040)-(1920,1080) */
const ROW_BOTTOM: Rect = { x: 0, y: 1040, width: 1920, height: 40 };
const DISPLAY_PLAIN: DisplayRects = { bounds: WA, workArea: WA }; // 无占位带：永远没有行
const DISPLAY_TOP: DisplayRects = { bounds: FULL, workArea: WA_ROW };
const DISPLAY_BOTTOM: DisplayRects = { bounds: FULL, workArea: WA };
/** 左右侧任务栏：那条带是竖直的，本形态不支持行内 */
const DISPLAY_LEFT: DisplayRects = {
  bounds: FULL,
  workArea: { x: 48, y: 0, width: 1872, height: 1080 }
};
const displayWith = (workArea: Rect): DisplayRects => ({ bounds: FULL, workArea });

describe('nearestEdges', () => {
  it('detects each edge within threshold', () => {
    expect(nearestEdges(bounds(0, 500), WA)).toEqual(['left']);
    expect(nearestEdges(bounds(1920 - WIN_W, 500), WA)).toEqual(['right']);
    expect(nearestEdges(bounds(500, 0), WA)).toEqual(['top']);
    expect(nearestEdges(bounds(500, 1040 - WIN_H), WA)).toEqual(['bottom']);
  });

  it('treats the 8px threshold as inclusive', () => {
    expect(nearestEdges(bounds(DOCK_THRESHOLD, 500), WA)).toEqual(['left']);
    expect(nearestEdges(bounds(DOCK_THRESHOLD + 1, 500), WA)).toEqual([]);
  });

  it('detects a corner as two edges', () => {
    expect(nearestEdges(bounds(2, 2), WA)).toEqual(['left', 'top']);
  });

  it('still reports docking after the window was pushed off screen', () => {
    // 贴边后透明边距被推出屏幕，窗口坐标为负；重新判定时仍应认定贴左
    expect(nearestEdges(bounds(-EDGE_MARGIN, 500), WA)).toEqual(['left']);
  });
});

describe('dock mask', () => {
  it('normalizes edge order', () => {
    expect(edgesToMask(['left', 'top'])).toBe('top-left');
    expect(edgesToMask(['bottom', 'right'])).toBe('bottom-right');
    expect(edgesToMask([])).toBeNull();
  });

  it('round-trips between mask and edges', () => {
    const all: DockMask[] = [
      'left',
      'right',
      'top',
      'bottom',
      'top-left',
      'top-right',
      'bottom-left',
      'bottom-right'
    ];
    for (const mask of all) {
      expect(edgesToMask(maskToEdges(mask))).toBe(mask);
    }
    expect(maskToEdges(null)).toEqual([]);
  });
});

describe('dockedPosition', () => {
  // 这一组只问透明边距推不推得出去，与行无关，因此统一用「无占位带的屏」。
  const dock = (b: Rect, mask: DockMask | null): { x: number; y: number } =>
    dockedPosition({ bounds: b, display: DISPLAY_PLAIN, mask, content: CARD });

  it('pushes the transparent margin off screen on the left', () => {
    // 可视卡片左缘应正好贴住工作区左边：窗口 x = -EDGE_MARGIN
    expect(dock(bounds(5, 500), 'left')).toEqual({ x: -EDGE_MARGIN, y: 500 });
  });

  it('pushes the transparent margin off screen on the right', () => {
    const { x } = dock(bounds(1545, 500), 'right');
    // 窗口右缘超出工作区 EDGE_MARGIN，卡片右缘正好贴住
    expect(x + WIN_W - EDGE_MARGIN).toBe(WA.width);
  });

  it('pushes the transparent margin off screen on the top', () => {
    expect(dock(bounds(500, 3), 'top').y).toBe(-EDGE_MARGIN);
  });

  it('keeps a one pixel inset at the bottom so the border is not clipped', () => {
    const { y } = dock(bounds(500, 999), 'bottom');
    const m = margins(bounds(500, 999), CARD);
    // 卡片下缘 = 工作区下缘 - DOCK_INSET，避免 1px 描边被裁掉
    expect(y + m.v + CARD.height).toBe(WA.height - DOCK_INSET);
    expect(m.h).toBe(EDGE_MARGIN);
  });

  it('pushes both margins on a corner dock', () => {
    expect(dock(bounds(2, 2), 'top-left')).toEqual({ x: -EDGE_MARGIN, y: -EDGE_MARGIN });
  });

  it('clamps an undocked window back into the work area', () => {
    // 落盘位置在屏幕外（显示器被移除等）时拉回可见区域
    expect(dock(bounds(-500, -500), null)).toEqual({ x: WA.x, y: WA.y });
    expect(dock(bounds(5000, 5000), null)).toEqual({
      x: WA.width - WIN_W,
      y: WA.height - WIN_H
    });
  });

  it('does not clamp the vertical axis while docked left and bottom', () => {
    expect(dock(bounds(0, 200), 'bottom-left')).toEqual({
      x: -EDGE_MARGIN,
      y: 1040 - WIN_H + EDGE_MARGIN - DOCK_INSET
    });
  });
});

describe('dockedPosition — 任务栏行内', () => {
  // 行矩形不再由调用方给出，所以每道题问的都是「这块屏 + 这个窗口矩形」的落点。
  const dock = (b: Rect, mask: DockMask | null, display = DISPLAY_TOP): { x: number; y: number } =>
    dockedPosition({ bounds: b, display, mask, content: CARD });

  it('卡片中心落在任务栏行内时，垂直居中到那一行，而不是贴回工作区上沿', () => {
    // 窗口高 38、行高 40 → y = 1，卡片中心 = 1 + 19 = 20 = 行中心
    expect(dock(bounds(500, 10), 'top')).toEqual({ x: 500, y: 1 });
  });

  it('卡片中心在行下方时仍贴工作区上沿，老语义不变', () => {
    // 中心 35+19=54 在行(0..40)之外 → 贴行下：y = 40 - m.v(6)
    expect(dock(bounds(500, 35), 'top').y).toBe(34);
  });

  it('中心正好压在行下沿时算"行外"，不吸进任务栏', () => {
    // 中心 21+19=40 = 行下沿，判据取左闭右开
    expect(dock(bounds(500, 21), 'top').y).toBe(34);
  });

  it('没有任务栏行（左右侧任务栏）时贴回工作区上沿', () => {
    // 左右侧那条带是竖直的，行内不支持 → 贴上边就是贴工作区上沿：y = 0 - m.v(6)
    expect(dock(bounds(500, 10), 'top', DISPLAY_LEFT).y).toBe(-EDGE_MARGIN);
    expect(dock(bounds(500, 10), 'top', DISPLAY_PLAIN).y).toBe(-EDGE_MARGIN);
  });

  it('行内仍可同时贴左：横向推出透明边距，纵向居中到行', () => {
    expect(dock(bounds(2, 10), 'top-left')).toEqual({
      x: -EDGE_MARGIN,
      y: 1
    });
  });
});

describe('dockedPosition — 任务栏在底部', () => {
  it('卡片中心落在底部行内时，同样居中到那一行', () => {
    // 中心 1045+19=1064 在行(1040..1080)内 → y = 1040 + (40-38)/2 = 1041
    expect(
      dockedPosition({
        bounds: bounds(500, 1045),
        display: DISPLAY_BOTTOM,
        mask: 'bottom',
        content: CARD
      })
    ).toEqual({
      x: 500,
      y: 1041
    });
  });

  it('中心在行上方时仍贴工作区下沿（保留 1px 描边内缩）', () => {
    const m = margins(bounds(500, 1000), CARD);
    expect(
      dockedPosition({
        bounds: bounds(500, 1000),
        display: DISPLAY_BOTTOM,
        mask: 'bottom',
        content: CARD
      }).y
    ).toBe(
      1040 - WIN_H + m.v - DOCK_INSET
    );
  });
});

// 本机实测值（175% DPI、任务栏在顶），不是从实现反推的期望值：
// 显示器 1463x915 DIP，工作区从 y=32 起；透明无边框窗被钳到 38 DIP（GetWindowRect 实测 67 物理px）。
// 吸附结果 y=-3 是 app 自己落盘的值（种 y=5，退出时存回 y=-3），不是算出来的。
const REAL_ROW: Rect = { x: 0, y: 0, width: 1463, height: 32 };
const REAL_WA: Rect = { x: 0, y: 32, width: 1463, height: 883 };
const REAL_WIN_H = 38;
const realBounds = (x: number, y: number): Rect => ({ x, y, width: 372, height: REAL_WIN_H });
const DISPLAY_REAL: DisplayRects = {
  bounds: { x: 0, y: 0, width: 1463, height: 915 },
  workArea: REAL_WA
};

describe('dockedPosition — 本机实测几何', () => {
  const dock = (b: Rect, mask: DockMask | null, centerInRow = false) =>
    dockedPosition({
      bounds: b,
      display: DISPLAY_REAL,
      mask,
      content: CARD,
      centerInRow
    });

  it('存 y=5 时吸附到 y=-3，与 app 实际落盘值一致', () => {
    // 中心 5 + 19 = 24 在行(0..32)内 → y = round((32-38)/2) = -3
    expect(dock(realBounds(700, 5), 'top')).toEqual({ x: 700, y: -3 });
  });

  it('吸附后卡片完整落在任务栏行内，溢出的只有透明边距', () => {
    const { y } = dock(realBounds(700, 5), 'top');
    expect(y).toBeLessThan(0); // 顶部透明边距溢出屏幕外
    expect(y + REAL_WIN_H).toBeGreaterThan(REAL_ROW.height); // 底部同样溢出
    const cardTop = y + (REAL_WIN_H - CARD.height) / 2;
    expect(cardTop).toBeGreaterThanOrEqual(0);
    expect(cardTop + CARD.height).toBeLessThanOrEqual(REAL_ROW.height);
  });

  it('存回 -3 后重启仍在行内：吸附是不动点，不会来回跳', () => {
    const once = dock(realBounds(700, 5), 'top');
    const twice = dock(realBounds(700, once.y), 'top');
    expect(twice).toEqual(once);
  });
});

// 行矩形派生（显示器 bounds − workArea）与**任务栏行内**判定。这两个函数原先住在
// widgetDock.ts 里、被 electron import 挡在单测之外，一条都没钉住；ADR-0005 的行内语义
// 搬进 dock.ts 之后才测得到。屏的夹具在上面「显示器簇」那一节。

describe('taskbarRow — 行矩形派生', () => {
  it('任务栏在顶：行就是显示器上沿那条占位带', () => {
    expect(taskbarRow(displayWith(WA_ROW), bounds(500, 10))).toEqual(ROW);
  });

  it('任务栏在底：行就是显示器下沿那条占位带', () => {
    expect(taskbarRow(displayWith(WA), bounds(500, 1045))).toEqual(ROW_BOTTOM);
  });

  it('上下同时有占位带时按卡片中心落在哪条带来选', () => {
    // 第三方 appbar 在顶 (0..40)、任务栏在底 (1040..1080)：中心在哪条带就吸哪条。
    // 选错的那条会把小窗甩到用户没拖去的那一侧。
    const both = displayWith({ x: 0, y: 40, width: 1920, height: 1000 });
    expect(taskbarRow(both, bounds(500, 10))).toEqual(ROW);
    expect(taskbarRow(both, bounds(500, 1045))).toEqual(ROW_BOTTOM);
  });

  it('中心不在任何带里时仍返回存在的那条带，由行内判据把它判掉', () => {
    const both = displayWith({ x: 0, y: 40, width: 1920, height: 1000 });
    expect(taskbarRow(both, bounds(500, 500))).toEqual(ROW);
    expect(cardInTaskbarRow(both, bounds(500, 500))).toBe(false);
  });

  it('左右侧任务栏时那条带是竖直的，本形态不支持行内', () => {
    // 左右两侧：工作区的 x 或宽度与显示器边界不一致，那条带是竖直的 → 没有行
    const narrow = displayWith({ x: 0, y: 0, width: 1800, height: 1080 });
    expect(taskbarRow(DISPLAY_LEFT, bounds(500, 10))).toBeNull();
    expect(taskbarRow(narrow, bounds(500, 10))).toBeNull();
    expect(cardInTaskbarRow(DISPLAY_LEFT, bounds(500, 10))).toBe(false);
    expect(cardInTaskbarRow(narrow, bounds(500, 10))).toBe(false);
  });

  it('工作区铺满显示器时没有行', () => {
    expect(taskbarRow(displayWith(FULL), bounds(500, 10))).toBeNull();
    expect(cardInTaskbarRow(displayWith(FULL), bounds(500, 10))).toBe(false);
  });

  it('本机几何：派生出的行矩形等于手抄的实测 REAL_ROW', () => {
    // REAL_ROW / REAL_WA 是从真机抄来的（1463x915 DIP、工作区从 y=32 起）。
    // 本条把「派生规则」钉在实测值上，而不是钉在实现自己吐出的数上。
    expect(taskbarRow(DISPLAY_REAL, realBounds(700, 5))).toEqual(REAL_ROW);
    expect(cardInTaskbarRow(DISPLAY_REAL, realBounds(700, 5))).toBe(true);
    // 同一块屏上悬浮在中间不算行内，因而 z-order 守卫不该启动
    expect(cardInTaskbarRow(DISPLAY_REAL, realBounds(700, 400))).toBe(false);
  });

  it('行内判据与 dockedPosition 吃的是同一条：压在行下沿上即判为行外', () => {
    // 中心 21+19=40 = 行下沿，左闭右开 ⇒ 行外 ⇒ dockedPosition 走"贴行下"
    expect(cardInTaskbarRow(DISPLAY_TOP, bounds(500, 21))).toBe(false);
    expect(
      dockedPosition({ bounds: bounds(500, 21), display: DISPLAY_TOP, mask: 'top', content: CARD }).y
    ).toBe(34);
    expect(cardInTaskbarRow(DISPLAY_TOP, bounds(500, 10))).toBe(true);
    expect(
      dockedPosition({ bounds: bounds(500, 10), display: DISPLAY_TOP, mask: 'top', content: CARD }).y
    ).toBe(1);
  });
});

// 行内居中（菜单「任务栏内水平居中」）：勾选且卡片中心在行内时，横向弹回行矩形水平中心。
// `centerInRow` = 该设置的当前值；顶行与底部占位带同一规则，不做任何托盘图标避让。
describe('dockedPosition — 行内居中', () => {
  const dock = (
    b: Rect,
    mask: DockMask | null,
    centerInRow: boolean | undefined,
    display: DisplayRects
  ) =>
    dockedPosition({ bounds: b, display, mask, content: CARD, centerInRow });

  it('勾选后卡片中心在顶行内：水平居中到行矩形，纵向仍垂直居中到行', () => {
    // 行宽 1920、窗宽 372 → x = (1920-372)/2 = 774；y = (40-38)/2 = 1
    expect(dock(bounds(500, 10), 'top', true, DISPLAY_TOP)).toEqual({ x: 774, y: 1 });
  });

  it('不勾选（显式 false 或省略）时横向仍由拖动决定，现状语义不变', () => {
    expect(dock(bounds(500, 10), 'top', false, DISPLAY_TOP).x).toBe(500);
    expect(dock(bounds(500, 10), 'top', undefined, DISPLAY_TOP).x).toBe(500);
  });

  it('居中对左右贴边取优先：行内贴左上角，松手也弹回水平居中', () => {
    // 勾选后行内的横向位置被规则锁住（想自由摆位要先取消勾选）
    expect(dock(bounds(2, 10), 'top-left', true, DISPLAY_TOP).x).toBe(774);
    expect(dock(bounds(1500, 10), 'top-right', true, DISPLAY_TOP).x).toBe(774);
  });

  it('卡片中心不在行内时居中标记不生效：贴行下语义与横向保持原样', () => {
    // 中心 35+19=54 在行(0..40)之外
    expect(dock(bounds(500, 35), 'top', true, DISPLAY_TOP)).toEqual({ x: 500, y: 34 });
  });

  it('没有行矩形时居中标记不生效（左右侧任务栏不支持行内，也就不居中）', () => {
    // 这块屏没有可用的行（那条带是竖直的），所以既不居中、x 也仍落在工作区内
    expect(dock(bounds(500, 10), 'top', true, DISPLAY_LEFT)).toEqual({ x: 500, y: -EDGE_MARGIN });
  });

  it('未贴顶/底边时不居中：居中依附于"进内"落点', () => {
    expect(dock(bounds(5, 500), 'left', true, DISPLAY_BOTTOM)).toEqual({
      x: -EDGE_MARGIN,
      y: 500
    });
  });

  it('底部行内同样水平居中', () => {
    // 中心 1045+19=1064 ∈ 行(1040..1080) → x=774，y=1040+(40-38)/2=1041
    expect(dock(bounds(500, 1045), 'bottom', true, DISPLAY_BOTTOM)).toEqual({ x: 774, y: 1041 });
  });

  it('本机几何：居中是不动点，重启二次结算不漂移', () => {
    // 行宽 1463、窗宽 372 → x = round((1463-372)/2) = 546
    const once = dock(realBounds(700, 5), 'top', true, DISPLAY_REAL);
    expect(once).toEqual({ x: 546, y: -3 });
    const twice = dock(realBounds(once.x, once.y), 'top', true, DISPLAY_REAL);
    expect(twice).toEqual(once);
  });
});

describe('needsZReassert', () => {
  // 只有两个条件同时成立才重申：在行内 + 被任务栏盖住
  it('行内且被任务栏盖住才重申', () => {
    expect(needsZReassert(true, true)).toBe(true);
  });

  it('悬浮态被盖住不去抢（那是正常 z-order）', () => {
    expect(needsZReassert(false, true)).toBe(false);
  });

  it('行内但没被盖住不动作', () => {
    expect(needsZReassert(true, false)).toBe(false);
  });

  it('两个都不成立不动作', () => {
    expect(needsZReassert(false, false)).toBe(false);
  });
});

describe('margins', () => {
  it('falls back to the card height when content size is unknown', () => {
    // 渲染端尚未上报尺寸时，用窗口尺寸反推，横向边距仍是 EDGE_MARGIN
    const m = margins(bounds(0, 0), null);
    expect(m.h).toBe(EDGE_MARGIN);
    // 兜底走的是 shared/types 的常量，不是上面那个场景值：改卡高必须联动这里
    expect(m.v).toBe(Math.round((WIN_H - WIDGET_CARD_HEIGHT) / 2));
  });
});

describe('windowSizingFor — 卡片尺寸 → 要请求的窗口尺寸', () => {
  // 本机透明无边框窗被 OS 钳到 ~37 DIP，而卡高由墨迹派生（默认字号下 20.6）。
  // 这两个数差着十几 px，谁吃哪一个不能靠运气：**请求值归这条函数，钳制后的真值不预测**——
  // 落尺寸之后由 widgetWindow controller 回读（ADR-0005 修正三：预测实测差 1~4 DIP，误差的一半直接变成偏心）。
  const SMALL: ContentSize = { width: 360, height: 17 };

  it('宽度 = 卡片 + 每边 EDGE_MARGIN，高度就是卡片高', () => {
    expect(windowSizingFor(bounds(0, 0), SMALL)).toEqual({
      width: 360 + 2 * EDGE_MARGIN,
      height: 17
    });
  });

  it('不再预测 OS 钳制：请求值与窗口现高无关', () => {
    // 钉住"顺手把预测加回来"的回归：窗口现高 38（钳制态）与 60（高于卡高）下请求值必须相同
    const clamped = bounds(0, 0);
    const roomy = { ...bounds(0, 0), height: 60 };
    expect(windowSizingFor(clamped, SMALL)).toEqual(windowSizingFor(roomy, SMALL));
    expect(windowSizingFor(clamped, SMALL).height).toBe(SMALL.height);
  });

  it('卡高超过窗口现高时也只报请求值（不拿现高夹它）', () => {
    const tall: ContentSize = { width: 360, height: 60 };
    expect(windowSizingFor(bounds(0, 0), tall)).toEqual({ width: 360 + 2 * EDGE_MARGIN, height: 60 });
  });

  it('渲染端还没上报尺寸时保持现有尺寸（redock 语义）', () => {
    const b = bounds(0, 0);
    expect(windowSizingFor(b, null)).toEqual({ width: WIN_W, height: WIN_H });
  });
});
