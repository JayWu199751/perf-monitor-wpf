/*
 * 角色分类的唯一出处。
 *
 * 为什么单独一个文件：run 的 key（`组名/种类 "文字"`）这套编码原先只有 fit-rho.cjs 认识，
 * 而 measure.cjs 也要开始按角色分派判据（上标角色不吃共线，改吃 cap 线）。同一份知识写两处
 * 一定会漂移——这条是 ADR-0006 里「判据有两处就一定会漂移」那个坑的同一个形状，所以两边都
 * require 这里。tests/opticalCenter.test.ts 钉住「CSS 两张表的条目 = 这里认识的角色」。
 */
const DEGREE = '\u00b0';

/**
 * 上标角色：它们的归位参照**不是**卡片中心线，而是「最大字样的墨迹上沿」（cap 线）。
 *
 * `°` 是上标小圈。把它抬到中心线上，就是用户报的那句「温度的单位应该在数字的右上角，
 * 你把它居中了」——共线定律对标签/数字/%/MB/s 都对，对带帽线语义的记号是错的。
 * 判据与反解各在哪一处执行见 measure.cjs 的「上标」与 fit-rho.cjs 的 referenceOf。
 */
const SUPERSCRIPT = new Set(['deg']);

/** 从组合后的 run key 里取种类（`CPU 2/unit "°"` → `unit`）。 */
function kindOf(key) {
  const slash = String(key).indexOf('/');
  return slash < 0 ? '' : String(key).slice(slash + 1).split(' ')[0];
}

/** 从组合后的 run key 里取文字（`CPU 2/unit "°"` → `°`）。 */
function textOf(key) {
  return (String(key).match(/"([^"]*)"/) || [])[1] || '';
}

/**
 * run → 角色，与 optical.css 里 `--a-<role>` / `--rho-<role>` 的 <role> 一一对应。
 * 划分理由（为什么 `%` 不并进 label、为什么 `--` 单独一档）写在 ADR-0006 修正四。
 */
function roleOf(key) {
  const kind = kindOf(key);
  const text = textOf(key);
  if (kind === 'unit') {
    if (text === DEGREE) return 'deg';
    if (text === 'MB/s') return 'mbs';
    if (text === '%') return 'pct';
    return 'label';
  }
  if (kind === 'num') return text === '--' ? 'dash' : 'text';
  if (kind === 'label') return 'label';
  if (kind === 'arrow') return 'arrow';
  return 'text';
}

function isSuperscript(key) {
  return SUPERSCRIPT.has(roleOf(key));
}

module.exports = { DEGREE, SUPERSCRIPT, kindOf, textOf, roleOf, isSuperscript };
