import { describe, expect, it } from 'vitest';
import { MIN_CARD_WIDTH, createSizeReporter } from '../src/renderer/src/lib/cardSize';
import { DEFAULT_SETTINGS } from '../src/main/settings';
import type { Settings } from '../src/shared/types';

function harness() {
  const sent: Array<{ width: number; height: number }> = [];
  const reporter = createSizeReporter((size) => sent.push(size));
  return {
    sent,
    measure: (width: number, height = 21, settings: Settings | null = DEFAULT_SETTINGS) =>
      reporter.update({ offsetWidth: width, offsetHeight: height }, settings)
  };
}

describe('尺寸上报完整变化序列', () => {
  it('布局实测取整且保留最小尺寸', () => {
    const h = harness();
    h.measure(371.2, 20.6);
    h.measure(371.8, 20.4);
    expect(h.sent).toEqual([{ width: 372, height: 21 }, { width: 372, height: 20 }]);
    const empty = harness();
    empty.measure(10, 0.2);
    expect(empty.sent).toEqual([{ width: MIN_CARD_WIDTH, height: 1 }]);
  });

  it('数字位数来回跳只扩宽一次，相同布局不重复上报', () => {
    const h = harness();
    [120, 140, 120, 120].forEach((w) => h.measure(w));
    expect(h.sent).toEqual([{ width: 120, height: 21 }, { width: 140, height: 21 }]);
  });

  it.each([
    { theme: 'dark' as const }, { opacity: 0.3 }, { transparentDisplay: true },
    { centerInTaskbarRow: true }, { refreshFastMs: 2000 }, { autoHideOnFullscreen: false }
  ])('读数变窄后无关设置 %j 不重置峰值', (patch) => {
    const h = harness();
    h.measure(400);
    h.measure(300);
    h.measure(300, 21, { ...DEFAULT_SETTINGS, ...patch });
    expect(h.sent).toEqual([{ width: 400, height: 21 }]);
  });

  it('关闭指标允许缩窄，新布局的自然变化仍只增宽', () => {
    const h = harness();
    const next = { ...DEFAULT_SETTINGS, metrics: { ...DEFAULT_SETTINGS.metrics, net: false } };
    h.measure(500);
    h.measure(300, 21, next);
    h.measure(320, 21, next);
    h.measure(300, 21, next);
    expect(h.sent.map((s) => s.width)).toEqual([500, 300, 320]);
  });

  it('换字号可双向改变宽高，观测和设置通知重复到达只报一次', () => {
    const h = harness();
    h.measure(400, 26);
    const small = { ...DEFAULT_SETTINGS, fontSize: 10 };
    h.measure(300, 17, small);
    h.measure(300, 17, structuredClone(small));
    expect(h.sent).toEqual([{ width: 400, height: 26 }, { width: 300, height: 17 }]);
  });

  it('DPI 等自然高度变化可双向，不丢失宽度峰值', () => {
    const h = harness();
    h.measure(400, 26);
    h.measure(300, 22);
    h.measure(300, 17);
    expect(h.sent).toEqual([
      { width: 400, height: 26 }, { width: 400, height: 22 }, { width: 400, height: 17 }
    ]);
  });

  it('初始设置未知按全量默认布局处理，只有实际不同才重置峰值', () => {
    const h = harness();
    h.measure(500, 21, null);
    h.measure(400);
    expect(h.sent).toHaveLength(1);
    h.measure(300, 21, { ...DEFAULT_SETTINGS, fontSize: 10 });
    expect(h.sent.at(-1)?.width).toBe(300);
  });
});
