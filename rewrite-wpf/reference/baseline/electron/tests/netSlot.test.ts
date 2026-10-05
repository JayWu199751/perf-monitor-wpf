import { describe, expect, it } from 'vitest';
import { NET_DIGITS, slotText } from '../src/renderer/src/lib/netSlot';

describe('slotText', () => {
  it('renders the missing state as two dashes', () => {
    expect(slotText(null)).toBe('--');
    expect(slotText(null, NET_DIGITS)).toBe('--');
  });

  it('keeps integers bare and applies the requested decimal places', () => {
    expect(slotText(37)).toBe('37');
    expect(slotText(1.25, NET_DIGITS)).toBe('1.3');
    expect(slotText(0, NET_DIGITS)).toBe('0.0');
  });
});

describe('net reading text', () => {
  // 定宽槽已撤掉（ADR-0006 修正七），所以这里守的是文字本身：一位小数、最长千兆峰值 5 字。
  // 宽度不再由常量预留——位数变化时的横向呼吸由 WidgetApp 的宽度只增不减兜底。
  it('keeps one decimal and stays within the widest real value', () => {
    expect(slotText(99.9, NET_DIGITS)).toBe('99.9');
    expect(slotText(125, NET_DIGITS)).toBe('125.0');
    expect(slotText(125, NET_DIGITS)).toHaveLength(5);
  });
});
