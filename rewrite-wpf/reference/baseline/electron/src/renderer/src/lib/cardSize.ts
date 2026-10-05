import type { Settings } from '../../../shared/types';

// 卡片尺寸：从「量到的盒子」到「该不该告诉主进程」。
//
// 主进程按上报的尺寸做贴边推出与任务栏行内判定（ADR-0003），所以这条规则是跨进程的：
// 报多一次就是一次原生窗口 resize，报错了位就闪一下。它此前内联在 WidgetApp.vue 的
// 两个函数里；现在布局取整、触发分类、峰值与去重都在同一个 module。

/** 卡片最小宽度（CSS px）：窄到没内容也要留一个可读的落点，避免抖动到 0 宽。 */
export const MIN_CARD_WIDTH = 64;

export interface CardSize {
  width: number;
  height: number;
}

/**
 * 只要求「量得到两个布局尺寸」，不写 `Pick<HTMLElement, ...>`：这样本模块在纯 node 的
 * vitest 里就能被 typecheck（tests 走的是 tsconfig.node.json，那里没有 DOM lib），
 * 而 `HTMLElement` 结构上本来就满足它。
 */
export interface MeasuredBox {
  offsetWidth: number;
  offsetHeight: number;
}

/**
 * 布局盒 → 卡片尺寸。宽向上取整（宁可多一格也不让文字被窗口边缘裁掉），高度四舍五入——
 * 高度不是常数：它由 `.bar` 裁到最大字样的墨迹外框派生（ADR-0006 修正三），换字号必须能双向变。
 */
function cardSizeOf(box: MeasuredBox): CardSize {
  return {
    width: Math.max(MIN_CARD_WIDTH, Math.ceil(box.offsetWidth)),
    height: Math.max(1, Math.round(box.offsetHeight))
  };
}

/**
 * 「什么时候报、报什么」。两条规则不对称，是有意的：
 *  - 宽度只增不减：数字位数变多时加宽，变窄时保留宽度，否则每次刷新都推动一次原生 resize，
 *    视觉上来回抖（老 bug，见 .scratch 的 issue 08）；
 *  - 高度可双向：卡高跟着字号呼吸，压不回旧高度就会把刚钉好的那格边距重新拉歪。
 */
export function createSizeReporter(report: (size: CardSize) => void) {
  let sent: CardSize = { width: 0, height: 0 };
  let layout: string | null = null;
  return {
    /** 只有字号或指标开关改变才重置峰值；主题、透明显示等变化保留历史宽度。 */
    update(box: MeasuredBox, settings: Pick<Settings, 'fontSize' | 'metrics'> | null): void {
      const key = `${settings?.fontSize ?? 12}:${(['cpu', 'mem', 'gpu', 'net', 'time'] as const)
        .map((name) => settings?.metrics[name] ?? true).join(',')}`;
      const reset = layout !== null && key !== layout;
      layout = key;
      const size = cardSizeOf(box);
      const next = { width: reset ? size.width : Math.max(sent.width, size.width), height: size.height };
      if (next.width === sent.width && next.height === sent.height) return;
      sent = next;
      report({ ...sent });
    }
  };
}
