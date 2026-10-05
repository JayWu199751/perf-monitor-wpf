/*
 * 从真像素 artifact 反解这张表：
 *
 *   抬量(css px) = -( a·(--fs) + rho·1em )      与 optical.css 的 .optical 同形
 *
 * 参照物默认是**卡片内容盒的中心线**（measure.cjs 每轮把 cardBox 一起写进 artifact），
 * 所以一次拟合同时管两件事：让各 run 的墨迹中线共线，并让那条线就是卡片中心线。
 * 唯一的例外是上标角色（`°`，名单在 roles.cjs）：它们的参照线换成「最大字样的墨迹上沿」
 * 即 cap 线。把上标小圈抬到中心线上，正是用户报的「单位被居中了」，见 ADR-0006 修正五。
 * 用法（先按 ADR-0006 的「关掉定律量自然位置」跑出一份 artifact）：
 *   node scripts/alignment/fit-rho.cjs scripts/alignment/natural.json
 * 每个角色两个数，是因为同流之后共享基线离中心线的那一截与 --fs 成正比；只写 rho·1em
 * 的话，字号被 clamp() 钉住的那几档（--fs 大于等于 15 的 .label/.unit、大于等于 16 的
 * 次级读数）会追不上，实测跨档极差 0.135em 约 2.8 设备px。
 *
 * 一条读表须知：对 em 始终与 --fs 成正比的角色，(a, rho) 的**分配**是弱可辨识的——
 * 挪 a 与 rho（rho 增减 delta/ratio，a 减 delta）预测不变。所以别把单个数字当物理量读，
 * 认的是「代回 optical.css 之后 measure:alignment 绿不绿」。改字号阶梯（clamp 上下限、
 * 0.82/0.9 这些比例）必须整表重跑；只改卡片高度不必——同流之后整条线一起挪。
 */
const fs = require('node:fs');

const artifact = process.argv[2] || 'scripts/alignment/natural.json';
const reps = JSON.parse(fs.readFileSync(artifact, 'utf8')).filter((r) => r.mode === 'widget' && r.cardBox);
if (!reps.length) throw new Error('artifact 里没有带 cardBox 的 widget 记录，先重跑量具');
const SF = reps[0].sf;
// 角色划分与 optical.css 的 --a-*/--rho-* 条目一一对应，唯一出处在 roles.cjs：
// 量具现在也要按角色分派判据（上标角色不吃共线），同一份知识写两处一定会漂移。
const { SUPERSCRIPT, roleOf } = require('./roles.cjs');

// 该 run 的**主体中线**最终该落在哪条线上（设备 px）。
//
// 中线口径与量具同源：用 midBody 而不是外框中线 midDev，因为外框会被细笔画（`MB/s` 的
// 斜杠降部）单方面拖走，按外框归位就让带降部的单位骑到数字的 cap 线上。
//
// 上标角色不吃这条线，它们要的是「自己的墨迹上沿坐在最大字样的墨迹上沿上」。注意那条
// cap 线在这里是**推出来的**、不是从这份 artifact 量出来的：artifact 是自然态（定律被
// DSM_CSS 关掉），那时数字还没被抬，直接拿它的 top 当参照会把数字自己的抬量漏算进去。
// 而定律保证最大字样的主体中线最终落在卡片中心线上，所以
//   cap 线 = 卡片中心线 − (最大字样墨迹高 − 1)/2
// 再加回本 run 自己的一半墨迹高，就是它的中线目标。
function capLineDev(rep) {
  const got = rep.runs.filter((r) => !r.missing && r.em);
  if (!got.length) return null;
  const maxEm = Math.max(...got.map((r) => r.em));
  // 定义 cap 线的那个 run = 最大字号档里墨迹上沿最高的那个（与 measure.cjs 的 mainTop 同口径）
  const main = got.filter((r) => r.em >= maxEm - 0.01).reduce((a, r) => (r.top < a.top ? r : a));
  return rep.cardBox.centerDev - (main.bottom - main.top) / 2;
}

function referenceOf(rep, r, role) {
  if (!SUPERSCRIPT.has(role)) return rep.cardBox.centerDev;
  const cap = capLineDev(rep);
  return cap === null ? null : cap + (r.bottom - r.top) / 2;
}

// 观测：把该 run 抬到它的参照线需要多少 CSS px（正 = 参照线在它下方，因为 y 向下增长）。
const obs = [];
for (const rep of reps) {
  for (const r of rep.runs) {
    if (r.missing || !r.em) continue;
    const role = roleOf(r.key);
    const ref = referenceOf(rep, r, role);
    if (ref === null) continue; // 这一轮连最大字样都没有，丢掉观测而不是编一条参照线
    obs.push({ role, fs: rep.size, em: r.em, y: (r.midBody - ref) / SF });
  }
}
const roles = [...new Set(obs.map((o) => o.role))].sort();

// 逐角色最小二乘：y = a·fs + rho·em。两列只有在角色跨多个字号档（含被 clamp 钉住的档）时
// 才线性无关；对始终成正比的角色会得到一个「和正确但比例任意」的解，故下面照例报出条件数。
const out = {};
for (const role of roles) {
  const pts = obs.filter((o) => o.role === role);
  let m = pts.reduce((a, p) => a + p.fs * p.fs, 0);
  let n = pts.reduce((a, p) => a + p.fs * p.em, 0);
  let q = pts.reduce((a, p) => a + p.em * p.em, 0);
  let u = pts.reduce((a, p) => a + p.fs * p.y, 0);
  let v = pts.reduce((a, p) => a + p.em * p.y, 0);
  const det = m * q - n * n;
  // 单档角色（em 始终与 --fs 成正比）两列线性相关，det 归零：这时 a 无法辨识，
  // 按定义把它记成 0，全部校正量落在 rho 上。这正是 optical.css 里 --a-base 的作用。
  const single = Math.abs(det) < 1e-6 * m * q;
  const a = single ? 0 : (u * q - v * n) / det;
  const rho = single ? v / q : (m * v - n * u) / det;
  const res = pts.map((p) => (p.y - a * p.fs - rho * p.em) * SF);
  out[role] = {
    a, rho,
    rms: Math.sqrt(res.reduce((s, r) => s + r * r, 0) / res.length),
    max: Math.max(...res.map(Math.abs)),
    cond: single ? Infinity : Math.max(m, q) / Math.min(m, q),
    tiers: new Set(pts.map((p) => (p.em / p.fs).toFixed(3))).size,
    n: pts.length
  };
}
console.log('artifact=' + artifact + '  sf=' + SF + '  roles=' + roles.join(','));
console.log('');
console.log('role      n  档数  条件数      A       B        rms   max   写进 optical.css 的常数');
for (const r of roles) {
  const o = out[r];
  console.log(r.padEnd(8) + String(o.n).padStart(4) + String(o.tiers).padStart(5) +
    (isFinite(o.cond) ? o.cond.toFixed(0) : 'inf').padStart(9) +
    o.a.toFixed(4).padStart(9) + o.rho.toFixed(4).padStart(9) + o.rms.toFixed(2).padStart(7) + o.max.toFixed(2).padStart(6) +
    ('  --a-' + r + ': ' + (-o.a).toFixed(4) + ';  --rho-' + r + ': ' + (-o.rho).toFixed(4) + ';').padStart(4));
}
console.log('');
console.log('注：这里拟合的是 vertical-align 本身 va = A·(--fs) + B·1em，A/B 即上表两列；');
console.log('    CSS 写成 calc(-1 * (a*--fs + rho*1em))，所以建议常数是取负后的 A/B。');
