// 对任意一份 artifact 复算「读数」：共线散差最坏值、散差随字号的最小二乘斜率、
// 以及这条斜率自己的噪声（σ_残差 与 3·SE）。用来跑「HEAD vs 修后」的同尺差分——
// 同一把尺子量两个状态，才说得清这次到底改掉了什么。
//
// 这里**不写判据**：上限、趋势的阈值与最终 RED/GREEN 只属于 measure.cjs（量具本体）。
// 上一版这个文件抄了一份 TOL=2.0 + 手拍的 TOL_SLOPE=0.08，而量具早已换成 2.5 与 3·SE，
// 于是同一个 artifact 两份脚本会给出相反结论——判据有两处，就一定会漂移。
// 趋势判据本身的可信度对照在 gate-check.cjs。
//
//   node scripts/alignment/diff-readout.cjs scripts/alignment/<HEAD 那份>.json scripts/alignment/last-report.json
const fs = require('node:fs');
const comboSpread = (rep) => Math.max(...rep.assert.map((g) => (g.dev ? g.dev.spread : 0)));

// 散差对字号做最小二乘：返回斜率 + 残差标准差 + 斜率标准误（SE = σ_残差 / √Σ(x−x̄)²）
function trend(pts) {
  const n = pts.length;
  const mx = pts.reduce((a, p) => a + p[0], 0) / n;
  const my = pts.reduce((a, p) => a + p[1], 0) / n;
  const sxx = pts.reduce((a, p) => a + (p[0] - mx) ** 2, 0);
  const sxy = pts.reduce((a, p) => a + (p[0] - mx) * (p[1] - my), 0);
  const slope = sxy / sxx;
  const resid = pts.map((p) => p[1] - (my + slope * (p[0] - mx)));
  const sigma = Math.sqrt(resid.reduce((a, r) => a + r * r, 0) / Math.max(1, n - 2));
  return { slope, sigma, se: sigma / Math.sqrt(sxx) };
}

for (const file of process.argv.slice(2)) {
  const reps = JSON.parse(fs.readFileSync(file, 'utf8')).filter((r) => r.mode === 'widget' && r.assert);
  const worst = reps.reduce((a, r) => Math.max(a, comboSpread(r)), 0);
  const lines = [];
  for (const st of [...new Set(reps.map((r) => r.state))]) {
    const pts = reps.filter((r) => r.state === st).map((r) => [r.size, comboSpread(r)]);
    if (pts.length < 3) continue;
    const t = trend(pts);
    lines.push(`${st} 斜率 ${t.slope.toFixed(3)}  3·SE ${(3 * t.se).toFixed(3)}（σ_残差 ${t.sigma.toFixed(2)}）`);
  }
  console.log(
    file.replace(/^.*[\\/]/, '').padEnd(26) +
      '组合 ' + String(reps.length).padStart(2) +
      '  散差最大 ' + worst.toFixed(2).padStart(5) + 'dev  ' + lines.join('  |  ')
  );
}
