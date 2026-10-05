import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 小窗只有一档字号（10–18）和两套主题，文字颜色全在 tokens.css 里。可读性没有渲染层接口
// 可测，但它是纯算术：把令牌解析出来算 WCAG 对比度，就能把「某一档灰度被调得太淡」钉在单测里。
// 用户真实报过的是亮色主题标签：0.48 时对卡片 3.06:1，连不透明卡片都不到 AA。
const TOKENS = readFileSync(resolve(__dirname, '../src/renderer/src/styles/tokens.css'), 'utf8');

function blockAfter(src: string, marker: string): string {
  const at = src.indexOf(marker);
  expect(at, '找不到 ' + marker).toBeGreaterThanOrEqual(0);
  return src.slice(at, src.indexOf('}', at));
}

/** 令牌要么是 #rrggbb，要么是 rgba(r, g, b, a)；两种都收，省得为改一种写法去改量具。 */
function colorOf(css: string, name: string): [number, number, number, number] {
  const hex = css.match(new RegExp('--' + name + ':\\s*#([0-9a-f]{6})', 'i'));
  if (hex) {
    const n = parseInt(hex[1], 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255, 1];
  }
  const m = css.match(new RegExp('--' + name + ':\\s*rgba\\(([^)]+)\\)'));
  expect(m, '找不到令牌 --' + name).not.toBeNull();
  const parts = m![1].split(',').map((x) => x.trim());
  const ch = (s: string) => (s.includes('%') ? (parseFloat(s) / 100) * 255 : parseFloat(s));
  return [ch(parts[0]), ch(parts[1]), ch(parts[2]), parts[3].includes('%') ? parseFloat(parts[3]) / 100 : parseFloat(parts[3])];
}

function rgbOf(css: string, name: string): [number, number, number] {
  const m = css.match(new RegExp('--' + name + ':\\s*(\\d+)\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)'));
  expect(m, '找不到令牌 --' + name).not.toBeNull();
  return [Number(m![1]), Number(m![2]), Number(m![3])];
}

const lin = (c: number) => {
  const s = c / 255;
  return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
};
const lum = ([r, g, b]: [number, number, number]) => 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
const over = (fg: [number, number, number, number], bg: [number, number, number]) =>
  [0, 1, 2].map((i) => fg[3] * fg[i] + (1 - fg[3]) * bg[i]) as [number, number, number];
function ratio(fg: [number, number, number, number], bg: [number, number, number]): number {
  const a = lum(over(fg, bg));
  const b = lum(bg);
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
}

const DARK = blockAfter(TOKENS, ':root {');
const LIGHT = blockAfter(TOKENS, '@media (prefers-color-scheme: light)');

describe('小窗文字对比度（令牌算术，卡片完全不透明）', () => {
  for (const [theme, css] of [['暗色', DARK], ['亮色', LIGHT]] as const) {
    it(theme + '：主读数 ≥ 7:1、次级与标签 ≥ 4.5:1', () => {
      const bg = rgbOf(css, 'bar-bg-rgb');
      const text = colorOf(css, 'bar-text');
      const secondary = colorOf(css, 'bar-secondary');
      const label = colorOf(css, 'bar-label');
      expect(ratio(text, bg), theme + ' 主读数').toBeGreaterThanOrEqual(7);
      expect(ratio(secondary, bg), theme + ' 次级读数').toBeGreaterThanOrEqual(4.5);
      // 标签是 0.82em（≈9.8px）+600，按 AA 的小字档要 4.5:1——它正是用户报过的那一档
      expect(ratio(label, bg), theme + ' 标签').toBeGreaterThanOrEqual(4.5);
    });
  }
});
