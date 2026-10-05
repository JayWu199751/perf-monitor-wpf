// 网速读数：一位小数（"0.0" ~ "125.0"）。
// 2026-09-30 起这里不再有定宽槽：右对齐槽把余量留在箭头与数字之间，读数一到 10.0 MB/s
// 就把「标签↔箭头」的可见间距吃掉一半（实测 13.31 → 2px）。现在宽度由内容给，
// 间距由标签自己的 margin 独占。取舍与实测见 ADR-0006 修正七。
export const NET_DIGITS = 1;

// 数值 -> 文本。null 为缺失态，显示 `--`（配合 is-missing 变暗）。
export function slotText(value: number | null, digits = 0): string {
  if (value == null) return '--';
  return digits > 0 ? value.toFixed(digits) : String(value);
}
