// 用同一套统计判据回头看 HEAD 的实测散差序列（第一次全扫的读数），
// 确认「3·SE」确实能把老 bug 判红——趋势判据的可信度就靠这一条对照。
const head = [1.00, 1.50, 1.50, 1.50, 1.50, 2.50, 2.50, 2.50, 2.50];   // HEAD fs10→18
const now = [0.50, 1.50, 1.00, 1.00, 1.50, 1.50, 1.50, 2.00, 1.00];    // 22px 修后
// 同一份 HEAD artifact，但按「同类均值」口径算散差（先对每一类文字取均值，再看类与类之间
// 多散；量具自己用的是全局口径 —— 所有 run 混在一起取最坏值）。这个对照曾经由已退役的
// fan-table.cjs 产出，读数抄在下面，本文件因此仍然自包含。
const headByClass = [1.00, 1.50, 1.50, 0.75, 0.75, 2.25, 2.13, 2.50, 2.25];
function stat(name, ys) {
  const xs = [10, 11, 12, 13, 14, 15, 16, 17, 18];
  const mx = 14, my = ys.reduce((a, b) => a + b, 0) / ys.length;
  const sxx = xs.reduce((a, x) => a + (x - mx) * (x - mx), 0);
  const slope = xs.reduce((a, x, i) => a + (x - mx) * (ys[i] - my), 0) / sxx;
  const resid = xs.map((x, i) => ys[i] - (my + slope * (x - mx)));
  const sigma = Math.sqrt(resid.reduce((a, r) => a + r * r, 0) / (ys.length - 2));
  const lim = Math.max((3 * sigma) / Math.sqrt(sxx), 0.02);
  console.log(name.padEnd(26) + 'slope=' + slope.toFixed(3) + '  σ=' + sigma.toFixed(2) +
    '  3·SE=' + lim.toFixed(3) + '  ⇒ ' + (slope > lim ? 'RED 显著张开' : 'ok'));
}
stat('HEAD (卡高26, 11常数)', head);
stat('修后 (卡高22, ρ 表)', now);
stat('HEAD 按类均值口径', headByClass);
console.log('\n注意：同一份 HEAD artifact，两种散差口径给出不同结论（全局口径 3·SE=0.102→RED，\n' +
  '按类均值口径 σ 更大 ⇒ 3·SE=0.198→ok）。所以趋势判据只能与它自己的口径一起成立——\n' +
  'measure.cjs 用的就是全局口径，换口径比较数值等于换了把尺子。');
