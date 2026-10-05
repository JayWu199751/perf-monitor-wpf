/*
 * 文字垂直居中量具（Phase 1 反馈环，非生产代码）
 *
 * 它断言用户报的三个症状，一个都不能少：
 * 1. 共线 —— 同一行各个文字 run 的「墨迹中线」是否落在同一条直线上（散差）。
 * 2. 居中 —— 墨迹**并集**的中心（并另报「主读数」即字号最大那批 run 的中线）是否落在
 *    卡片内容盒的中线上。第 1 条只保证「齐」，不保证「在中间」：全组一起往下漂 1.6 CSS px，
 *    散差照样是 0 —— 这正是旧版量具漏掉的那一格。
 * 3. 边距 —— 「最大的字样」（字号等于 --fs 那批主读数 run）的墨迹上下沿到卡片可视边框的距离，
 *    是否钉在目标值上（默认 8 CSS px，必须与 shared/types.ts 的 WIDGET_EDGE_GAP 同步改）。
 *    这一条判据是「卡高该随字号走」的理由，每行还直接报出「应给高」= 最大字样墨迹高 +
 *    上下各一个目标边距 + 2px 透明边框，改目标边距时拿它对账卡高。
 * 4. 上标 —— `°` 这类上标记号的墨迹**上沿**是否坐在最大字样的墨迹上沿（cap 线）上。
 *    它不参与第 1 条：共线定律对标签/数字/%/MB/s 都对，对带帽线语义的记号是错的——
 *    把 `°` 抬到中心线上就是用户报的「温度的单位应该在数字的右上角，你把它居中了」。
 *    名单在 roles.cjs，反解器 fit-rho.cjs 与量具共用它，所以「谁豁免、谁改吃这条」只有一处。
 * 四条判据都吃**字体栈**这条轴（DSM_FONTS）：常数只要是从某一族字体拟合出来的，
 * 换一族字体就该在这里红。判据来自真像素而非排版推算——
 * 用真 DPI、真字体栈、真 DOM 渲染出真卡片，capturePage 抓合成后的位图，
 * 再按每个 run 的横向取值带逐行找墨迹的上下沿，取中点。
 *
 * 用法（在 electron/ 下）：
 *   npm run measure:alignment                 # 两个窗 × 字号 10–18 × 有值/缺失态
 *   DSM_MODES=widget DSM_SIZES=12 DSM_STATES=values npm run measure:alignment   # 单组合
 *   DSM_FONTS="auto,'Microsoft YaHei',Arial" ...                # 加一条字体栈轴
 *   DSM_PNG=1 ...                             # 每组合把卡片区域存成 PNG 供肉眼核对
 * 退出码：共线散差 / 居中误差 / 边距误差 / 上标误差 / 裁切 任一超标 → 1；量具自身出错 → 2。
 */
const { app, BrowserWindow, nativeTheme } = require('electron');
const path = require('node:path');
const fs = require('node:fs');
const { SUPERSCRIPT, roleOf } = require('./roles.cjs');

const HERE = __dirname;
const RENDERER = path.resolve(HERE, '..', '..', 'out', 'renderer');
const PRELOAD = path.join(HERE, 'preload.cjs');
const FIXTURE = path.join(HERE, '.fixture.json');
const REPORT = path.join(HERE, 'last-report.json');
const COLLECT_SRC = '(' + require('./collect.cjs').toString() + ')()';

// 允差 2.5 设备px。依据与换尺子的历史：Chromium 把文本基线各自吸附到设备网格，实测自然
// 中线只落在 0.5 设备px 的格点上、vertical-align 的抬量也只能整格生效 ⇒ 共线极限在 0.5~1
// 之间，逐档再紧就成了调格点。旧尺子（外框中线）下 HEAD 读 2.0；换成**主体中线**之后同一份
// 渲染读到 2.5，不是变差，是尺子终于看得见用户报的那一格：`MB/s` 按外框量是「齐」的，
// 按主体量它离中线 1.5 设备px。同一把新尺子下，改之前是 3.32~4.85，改之后最差 2.5。
// 但老 bug 的特征不是绝对值，而是**趋势**：扇形随字号单调张开
// （HEAD 实测 fs10=1.0 → fs15=4.0）。所以上限与趋势两条都要卡住，缺一挡不住回归。
const TOL = Number(process.env.DSM_TOL || 2.5); // 单组合上限，设备像素
// （原 DSM_TOL_SLOPE 手拍阈值已废弃：它落在格点抖动本身能造出的量级里，
//  改为按实测残差算 3·SE，见下方趋势判据）
const MODES = (process.env.DSM_MODES || 'widget,settings').split(',');
const SIZES = (process.env.DSM_SIZES || '10,11,12,13,14,15,16,17,18').split(',').map(Number);
const STATES = (process.env.DSM_STATES || 'values,missing').split(',');
const PAD = Number(process.env.DSM_PAD || 4); // 纵向搜索余量，CSS px
const INSET = Number(process.env.DSM_INSET || 0.4); // 横向内缩，CSS px

// 字体栈轴。'auto' = 不动源码里的那一栈（生产形态）；其余每项用 body{font-family:...!important}
// 整栈换掉，考察「常数是否只对本机这一族字体成立」。逗号在 env 里是分隔符，
// 所以栈内多族要写成 'A|B'（下面还原成 'A','B'）。
const FONTS = (process.env.DSM_FONTS || 'auto').split(',');
function fontDeclaration(font) {
  if (!font || font === 'auto') return '';
  const families = font.split('|').map((f) => (f.trim().startsWith("'") ? f.trim() : "'" + f.trim() + "'"));
  // 每族都追加一个 generic 兜底：某些族（Consolas/宋体）没有 CJK 字形，
  // 不兜底会让中文落到系统默认，量的就不是「这一栈」而是「这一栈 + 未知回落」。
  const stack = families.concat(['sans-serif']).join(',');
  return 'body{font-family:' + stack + ' !important}';
}

// 居中判据按 CSS px 报，但它的**地板由设备网格决定**，所以允差要跟着 DPR 归一后再换回 CSS px。
// 依据：共享基线被 Chromium 吸附到设备网格，中线只能落在 0.5 设备px 的格点上；而这条定律
// 自己的极限就是「p90 0.50 / 最大 1.00 设备px」（ADR-0006 修正二）。允差一旦比 1 设备px 还紧，
// 在低 DPR 机器上「红」就只反映格点相位、不反映回归——量具于是失去分辨力。
// 旧口径把这个数写死成 0.5 CSS px：撰写时那台机器是 175%（0.5 CSS px = 0.875 设备px，
// 正好压在下限上），换到 125% 的机器就变成 0.625 设备px，比地板还紧，实测 fs15 的
// ±0.75 CSS px（= 0.94 设备px）格点抖动会平白报红。现在地板取 1 设备px、按本轮实测 sf 折算，
// DSM_CENTER_TOL 退化成「更松的 CSS 下限」（默认 0 = 不额外放宽）。
const CENTER_TOL = Number(process.env.DSM_CENTER_TOL || 0); // CSS px 下限（0 = 只吃网格地板）
const CENTER_TOL_DEV = Number(process.env.DSM_CENTER_TOL_DEV || 1); // 网格地板，设备像素
const MAIN_CENTER_TOL = Number(process.env.DSM_MAIN_TOL || 0); // 主读数（最大字号那一档）单独一档
// 边距目标 = shared/types.ts 的 WIDGET_EDGE_GAP，两处必须一起改（改这里才能让量具继续说真话）
const GAP_TARGET = Number(process.env.DSM_GAP_TARGET || 6); // 最大字样墨迹到可视边框的目标
// 允差 ±0.6 CSS px 的依据：基线取整本身给 ±0.29（0.5 设备px），数字 0/6/8/9 在 cap 线之上
// 还各有一点过冲，两者合起来就是 ±0.5 上下；再紧就成了拿量具去凑某一档的整数。
const GAP_TOL = Number(process.env.DSM_GAP_TOL || 0.6);
// 上标判据的允差，设备 px。依据：抬量只能以整设备px 生效（修正二），而这条一次要过两个
// 取整面 —— 上标 run 自己的上沿与最大字样的上沿各自吸附一回，合起来就是 ±1 设备px 的格点
// 抖动；再留半格余量。被守的那个 bug 量级是 5 设备px（`°` 从中线被抬回 cap 线），
// 所以这条门再松也照样抓得住，紧只是为了别让「差一整格」溜过去。
const SUP_TOL = Number(process.env.DSM_SUP_TOL || 1.5);
const SHOTS = process.env.DSM_PNG ? path.join(HERE, 'shots') : null;

// DSM_SEGS 把可见段收窄（走 settings.metrics 开关这条路），用于把复现削到
// 「最小仍然变红」的单元
const SEGS = (process.env.DSM_SEGS || 'cpu,mem,gpu,net,time').split(',');

function settingsFor(size) {
  return {
    widget: { x: 0, y: 0, docked: null },
    metrics: {
      cpu: SEGS.includes('cpu'),
      mem: SEGS.includes('mem'),
      gpu: SEGS.includes('gpu'),
      net: SEGS.includes('net'),
      time: SEGS.includes('time')
    },
    refreshFastMs: 1000,
    refreshSlowMs: 5000,
    autostart: false,
    autoHideOnFullscreen: false,
    centerInTaskbarRow: false,
    opacity: 1, // 卡片底完全不透明，墨迹对比度最大，像素判据最干净
    fontSize: size,
    theme: 'dark'
  };
}

function metricsFor(state) {
  const on = state !== 'missing';
  const n = (v) => (on ? v : null);
  return {
    cpuPct: 45,
    cpuTemp: n(85),
    memUsedGb: 12.4,
    memTotalGb: 32,
    memPct: 39,
    gpuPct: n(23),
    gpuMemPct: n(41),
    gpuTemp: n(61),
    netDownMBs: n(1.24),
    netUpMBs: n(0.03),
    ts: 1700000000000
  };
}

// —— 像素判据 ————————————————————————————————————————————————
function lumAt(bmp, i) {
  // capturePage 位图是 BGRA
  return (bmp[i] * 299 + bmp[i + 1] * 587 + bmp[i + 2] * 114) / 1000;
}

function inkMidline(bmp, devW, devH, sf, run) {
  const x0 = Math.ceil(run.x0 * sf + INSET * sf);
  const x1 = Math.floor(run.x1 * sf - INSET * sf);
  const y0 = Math.max(0, Math.floor((run.y0 - PAD) * sf));
  const y1 = Math.min(devH - 1, Math.ceil((run.y1 + PAD) * sf));
  if (x1 - x0 < 2 || y1 - y0 < 2) return null;

  let lo = Infinity;
  let hi = -Infinity;
  const hist = new Int32Array(256);
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      const l = lumAt(bmp, (y * devW + x) * 4) | 0;
      hist[l]++;
      if (l < lo) lo = l;
      if (l > hi) hi = l;
    }
  }
  if (hi - lo < 40) return null; // 整带无对比度：没抓到字
  let bg = lo;
  let best = -1;
  for (let i = 0; i < 256; i++) if (hist[i] > best) { best = hist[i]; bg = i; }
  const ink = hi - bg > bg - lo ? hi : lo;
  const thr = bg + (ink - bg) / 2;

  const n = y1 - y0 + 1;
  const bandW = x1 - x0 + 1;
  const cov = new Float64Array(n); // 每行墨迹覆盖率（0..1），亚像素边沿要用它插值
  for (let y = y0; y <= y1; y++) {
    let c = 0;
    for (let x = x0; x <= x1; x++) if ((lumAt(bmp, (y * devW + x) * 4) > thr) === (ink > bg)) c++;
    cov[y - y0] = c / bandW;
  }
  // 1) 行盒判据：取最长连续「有墨」行段，避开邻行/发丝线的零星像素
  let top = -1;
  let bottom = -1;
  let runTop = -1;
  for (let i = 0; i < n; i++) {
    if (cov[i] > 0) {
      if (runTop < 0) runTop = i;
      if (bottom < 0 || i - runTop > bottom - top) { top = runTop; bottom = i; }
    } else runTop = -1;
  }
  if (top < 0) return null;
  // 2) 亚像素判据：在最长行段内用「半峰」插值求上下边沿。
  //    行段中线只能落在 .0/.5 两档（±0.5 设备px 量化），而中线差是我们要测的量，
  //    所以必须有一条连续估计量；弧形顶/底的偏置上下对称，取中点后相消。
  const peak = Math.max(...Array.prototype.slice.call(cov.subarray(top, bottom + 1)));
  const half = peak / 2;
  // 3) 「主体行段」判据：外框中线不等于看着居中的那条线。
  //    `MB/s` 的斜杠只占取值带一两列，却把外框下沿拖下去一整格，按外框居中反而让 M B s
  //    骑在数字的 cap 线上（用户截图实测：两者墨迹上沿同一行，主体差 1.5 设备px）。
  //    主体 = 覆盖率 ≥ 20% 峰值的最长连续行段。但这条规则对 `↓↑` 必须退回外框：箭头是
  //    一根通高的细杆，整根杆身都不过阈值，主体会塌成箭头那三行（实测散差被推到 9 设备px）。
  //    所以加一条形状自检：**主体撑不满外框 60% 高度就按外框算**——斜杠降部只是外框的一小截
  //    （MB/s 主体 12/14 行，用主体），杆身就是全部（箭头 3/14 行，用外框）。
  //    视觉重心（Σ cov·y ÷ Σ cov）也试过：对 MB/s 对，对箭头错得更狠——↑ 的墨在上半、
  //    ↓ 的墨在下半，重心把两个相反方向各拉 2~3 设备px，而它们的外框本来就是对齐的。
  const bodyThr = peak * 0.2;
  let bTop = -1;
  let bBot = -1;
  let segTop = -1;
  for (let i = top; i <= bottom; i++) {
    if (cov[i] >= bodyThr) {
      if (segTop < 0) segTop = i;
      if (bBot < 0 || i - segTop > bBot - bTop) { bTop = segTop; bBot = i; }
    } else segTop = -1;
  }
  // 阈值取 75%：`MB/s` 的主体是 12/14 行（86%，用主体），箭头即便算上杆身也只到 3~7/14
  // 行（≤50%，退回外框）。0.6 那一版正好落在箭头的摆动区间里，实测让箭头在部分档位
  // 换算法则，散差从 2.0 抖到 2.5 设备px。
  const bodyIsReal = bTop >= 0 && (bBot - bTop + 1) >= 0.75 * (bottom - top + 1);
  const midBody = bodyIsReal ? y0 + (bTop + bBot) / 2 : y0 + (top + bottom) / 2;
  const cross = (from, to, step) => {
    let prev = -1;
    for (let i = from; i !== to; i += step) {
      if (cov[i] >= half) {
        if (prev < 0) return i; // 端点即峰值：无从插值
        return prev + (half - cov[prev]) / ((cov[i] - cov[prev]) || 1) * step + (step < 0 ? 1 : 0);
      }
      prev = i;
    }
    return null;
  };
  const up = cross(top, bottom + 1, 1);
  const down = cross(bottom, top - 1, -1);
  const midSub = up === null || down === null ? y0 + (top + bottom) / 2 : y0 + (up + down) / 2;
  return {
    top: y0 + top,
    bottom: y0 + bottom,
    midDev: y0 + (top + bottom) / 2,
    midBody: +midBody.toFixed(3),
    bodyHeightDev: bodyIsReal ? bBot - bTop + 1 : bottom - top + 1,
    midSub: midSub,
    peakCov: +peak.toFixed(3),
    midCss: (y0 + (top + bottom) / 2) / sf,
    midSubCss: midSub / sf,
    midBodyCss: +(midBody / sf).toFixed(3),
    heightDev: bottom - top + 1,
  };
}

function median(xs) {
  const s = xs.slice().sort((a, b) => a - b);
  const m = s.length >> 1;
  return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2;
}

function spreadsOf(items, field) {
  if (items.length < 2) return null;
  const mids = items.map((i) => i[field]);
  const ref = median(mids);
  const worst = items
    .map((i) => ({ key: i.key, dev: Math.abs(i[field] - ref) }))
    .sort((a, b) => b.dev - a.dev)[0];
  return {
    n: items.length,
    ref: ref,
    min: Math.min(...mids),
    max: Math.max(...mids),
    spread: Math.max(...mids) - Math.min(...mids),
    worst: worst.key,
    worstDev: worst.dev
  };
}

// —— 一轮测量 ————————————————————————————————————————————————
function withTimeout(p, ms, label) {
  return Promise.race([
    p,
    new Promise((_, rej) => setTimeout(() => rej(new Error('timeout ' + label)), ms).unref?.())
  ]);
}

async function measureOnce(win, mode, size, state, font) {
  fs.writeFileSync(FIXTURE, JSON.stringify({ settings: settingsFor(size), metrics: metricsFor(state) }));
  const file = mode === 'widget' ? 'widget.html' : 'settings.html';
  await win.loadFile(path.join(RENDERER, file));
  await withTimeout(win.webContents.executeJavaScript(
    'new Promise((r)=>requestAnimationFrame(()=>requestAnimationFrame(()=>setTimeout(r,60))))'), 5000, 'frames');

  // 字体栈注入：在定律实验之前，好让 DSM_CSS 仍能在必要时压过它。
  const fontCss = fontDeclaration(font);
  if (fontCss) {
    const css = JSON.stringify(fontCss);
    await win.webContents.executeJavaScript(
      'var f=document.createElement("style");f.id="dsm-font";f.textContent=' + css + ';' +
      'document.getElementById("dsm-font")&&document.getElementById("dsm-font").remove();' +
      'document.head.appendChild(f);' +
      'new Promise((r)=>requestAnimationFrame(()=>requestAnimationFrame(()=>setTimeout(r,60))))');
  }

  // DSM_CSS：一次性实验开关——注入样式后重排，只改这一个变量。
  // 用它做「剥掉全部手调抬量」的纯基线实验，不动任何组件源码。
  // tests/opticalCenter.test.ts 那条「关掉定律重测」的注入样式就走这里。
  if (process.env.DSM_CSS) {
    const css = JSON.stringify(process.env.DSM_CSS);
    await win.webContents.executeJavaScript(
      'var s=document.createElement("style");s.id="dsm-override";s.textContent=' + css +
      ';document.getElementById("dsm-override")&&document.getElementById("dsm-override").remove();' +
      'document.head.appendChild(s);' +
      'new Promise((r)=>requestAnimationFrame(()=>requestAnimationFrame(()=>setTimeout(r,60))))');
  }
  const dom = await win.webContents.executeJavaScript(COLLECT_SRC);
  let img = await withTimeout(win.webContents.capturePage(), 10000, 'capturePage(hidden)');
  if (!img || img.isEmpty()) {
    console.log('  [instrument] 隐藏窗抓帧为空，改为离屏位置显示后重试');
    win.setVisibleOnAllWorkspaces(true);
    win.showInactive();
    win.setPosition(-30000, -30000);
    await withTimeout(win.webContents.executeJavaScript(
      'new Promise((r)=>requestAnimationFrame(()=>requestAnimationFrame(()=>setTimeout(r,120))))'), 5000, 'frames');
    img = await withTimeout(win.webContents.capturePage(), 10000, 'capturePage(visible)');
  }
  // sf 一律从「位图总像素 / 视口 CSS 尺寸」反推：capturePage 的 getSize()/
  // getScaleFactor() 语义在 HiDPI 下不可信（实测 1.75 屏上报 sf=1），
  // 而视口 CSS 尺寸与位图实际像素数是硬事实。
  const bmp = img.toBitmap ? img.toBitmap() : img.getBitmap();
  const px = bmp.length / 4;
  const sf = Math.sqrt(px / (dom.viewport.w * dom.viewport.h));
  const devW = Math.round(dom.viewport.w * sf);
  const devH = Math.round(dom.viewport.h * sf);
  if (Math.abs(sf - dom.dpr) / dom.dpr > 0.05) {
    throw new Error('INSTRUMENT: sf=' + sf.toFixed(3) + ' 与 devicePixelRatio=' + dom.dpr +
      ' 不一致，抓帧尺寸不可信');
  }
  if (dom.scroll.y !== 0 || dom.scroll.x !== 0) {
    throw new Error('INSTRUMENT: 页面存在滚动，取值带 y 需先平移');
  }

  const runs = [];
  for (const r of dom.runs) {
    const m = inkMidline(bmp, devW, devH, sf, r);
    if (!m) { runs.push({ key: r.group + '/' + r.key + ' "' + r.text + '"', missing: true }); continue; }
    runs.push({
      key: r.group + '/' + r.key + ' "' + r.text + '"',
      role: roleOf(r.group + '/' + r.key + ' "' + r.text + '"'),
      group: r.group,
      band: [+(r.x0 * sf).toFixed(1), +(r.x1 * sf).toFixed(1)],
      ywin: [+(r.y0 * sf).toFixed(1), +(r.y1 * sf).toFixed(1)],
      em: r.em,
      weight: r.weight,
      lineHeight: r.lineHeight,
      va: r.va,
      tf: r.tf,
      rectH: r.rectH,
      A: r.A,
      D: r.D,
      inkA: r.a,
      inkD: r.d,
      inkHeightDev: m.heightDev,
      peakCov: m.peakCov,
      top: m.top,
      bottom: m.bottom,
      midDev: +m.midDev.toFixed(3),
      midBody: m.midBody,
      midSub: +m.midSub.toFixed(3),
      midCss: +m.midCss.toFixed(3),
      midBodyCss: m.midBodyCss,
      midSubCss: +m.midSubCss.toFixed(3)
    });
  }
  const got = runs.filter((r) => !r.missing);
  const byGroup = {};
  got.forEach((r) => { (byGroup[r.group] = byGroup[r.group] || []).push(r); });
  // 断言分组：小窗整条 bar 视觉上就是「一行」，全局共线即用户报的症状；
  // 设置窗各行上下堆叠，全局共线无意义，只有同一行内共线才算问题。
  // 上标角色（`°`）从这条判据里**出局**：它本来就不该和别人的中线齐，它齐的是 cap 线，
  // 见下面第 4 条。留着它只会让散差量到「定律对新参照也归了位」这一件好事并把它判红。
  const collinear = got.filter((r) => !SUPERSCRIPT.has(r.role));
  const assertGroups =
    mode === 'widget'
      ? [{ name: 'bar', items: collinear }]
      : Object.entries(byGroup)
          .map(([name, items]) => ({ name, items: items.filter((r) => !SUPERSCRIPT.has(r.role)) }));
  // 裁切判据：真实卡片窗口高 == WIDGET_CARD_HEIGHT，内容盒是 border-box 内缩 1px。
  // 墨迹越出内容盒即被切；顺带报最紧余量，供「还能再降多少」决策。
  let clip = [];
  let headroom = null;
  let cardBox = null;
  let center = null;
  let superscript = null;
  if (dom.card && got.length) {
    const contentTop = (dom.card.top + 1) * sf;
    const contentBottom = (dom.card.bottom - 1) * sf;
    cardBox = {
      topDev: +contentTop.toFixed(3),
      bottomDev: +contentBottom.toFixed(3),
      centerDev: +((contentTop + contentBottom) / 2).toFixed(3)
    };
    clip = got.filter((r) => r.top < contentTop - 0.01 || r.bottom > contentBottom + 0.01).map((r) => r.key);
    const gaps = got.map((r) => Math.min(r.top - contentTop, contentBottom - r.bottom));
    headroom = {
      minEdgeGapDev: +Math.min(...gaps).toFixed(2),
      cardHeightCss: +dom.card.height.toFixed(2),
      cardWidthCss: +(dom.card.width == null ? NaN : dom.card.width).toFixed(2),
      tallestInkCss: +(Math.max(...got.map((r) => r.inkHeightDev)) / sf).toFixed(2)
    };
    // —— 居中判据：整条 bar 墨迹**并集**的中心离卡片中线有多远 ——
    // 散差只管「齐不齐」，这条管「在不在中间」，两者互不替代。
    const unionTop = Math.min(...got.map((r) => r.top));
    const unionBottom = Math.max(...got.map((r) => r.bottom));
    // 同时看参照的两个口径：并集中心（决定「贴不贴边」这条判据落在哪）与
    // 主读数（字号最大、视觉权重最高的那批 run）的中线。后者才回答「最大的字样居中了没」。
    const maxEm = Math.max(...got.map((r) => r.em || 0));
    const mains = got.filter((r) => r.em && r.em >= maxEm - 0.01);
    // —— 边距判据：墨迹**并集**的上下沿到卡片可视边框外沿的距离 ——
    // 参照取两把尺子：mains = 字号等于 --fs 的那批 run（「最大的字样」，用户量的就是它），
    // union = 全条 bar 的墨迹并集（最紧的那条，用来盯别的角色会不会被裁）。
    // 卡片的面板外沿（background-clip: padding-box 加那圈描边）就在内容盒那条线上——
    // 1px 透明边框在它之外，不是可视边缘——所以 contentTop/contentBottom 就是可视边框，
    // 不再另加那 1px。
    const mainTop = Math.min(...mains.map((r) => r.top));
    const mainBottom = Math.max(...mains.map((r) => r.bottom));
    const gapTop = (mainTop - contentTop) / sf;
    const gapBottom = (contentBottom - mainBottom) / sf;
    center = {
      unionErrCss: +(((unionTop + unionBottom) / 2 - cardBox.centerDev) / sf).toFixed(3),
      mainErrCss: mains.length
        ? +((median(mains.map((r) => r.midBody)) - cardBox.centerDev) / sf).toFixed(3)
        : null,
      mainN: mains.length,
      mainHeightCss: mains.length ? +((mainBottom - mainTop + 1) / sf).toFixed(2) : null,
      unionHeightCss: +((unionBottom - unionTop + 1) / sf).toFixed(2),
      gapTopCss: +gapTop.toFixed(3),
      gapBottomCss: +gapBottom.toFixed(3),
      minCss: +(Math.min(...gaps) / sf).toFixed(3),
      // 「要把边距钉在 GAP_TARGET，卡高该给多少」= 最大字样的墨迹高 + 上下各一个目标边距
      // 再加上下各 1px 透明边框。卡高改成墨迹派生之后，这条只用来对账。
      wantCardHCss: +((mainBottom - mainTop + 1) / sf + 2 * GAP_TARGET + 2).toFixed(2)
    };
    // —— 上标判据：`°` 的墨迹**上沿**有没有坐在最大字样的墨迹上沿（cap 线）上 ——
    // 这条替代第 1 条对这些角色的位置：它们不参与共线（上面已从 assert 组里出局）。
    // 参照取 mainTop 而不是「自己那个读数」的数字上沿，理由有两条：① 缺失态那一格是
    // `--`，它的墨迹坐在数学轴上，拿它当参照会让 `°` 随有没有数据上下跳；② 全条 bar 的
    // 上标记号落在同一条线上才是「设计过的」，cap 线也正是 .bar 的 text-box 裁出来的那条线。
    const sups = got.filter((r) => SUPERSCRIPT.has(r.role));
    if (sups.length) {
      superscript = {
        n: sups.length,
        capTopDev: +mainTop.toFixed(3),
        maxAbsDev: +Math.max(...sups.map((r) => Math.abs(r.top - mainTop))).toFixed(3),
        worst: sups
          .map((r) => ({ key: r.key, topDev: r.top, errDev: +(r.top - mainTop).toFixed(3) }))
          .sort((a, b) => Math.abs(b.errDev) - Math.abs(a.errDev))[0].key
      };
    }
    // 肉眼核对用：把卡片那一条连同上下各 3px 存成 PNG（真 DPI、真合成位图）。
    if (SHOTS) {
      const y = Math.max(0, Math.floor((dom.card.top - 3) * sf));
      const h = Math.min(devH - y, Math.ceil((dom.card.height + 6) * sf));
      const cropped = img.crop({ x: 0, y, width: devW, height: h });
      fs.mkdirSync(SHOTS, { recursive: true });
      fs.writeFileSync(
        path.join(SHOTS, [mode, 'fs' + size, state, (font || 'auto').replace(/[^\w.-]+/g, '_') + '.png']
          .filter(Boolean).join('-')),
        (cropped.isEmpty && cropped.isEmpty()) ? img.toPNG() : cropped.toPNG()
      );
    }
  }
  return {
    mode, size, state, font: font || 'auto',
    dpr: dom.dpr, sf: +sf.toFixed(4),
    clip,
    cardBox,
    headroom,
    center,
    superscript,
    assert: assertGroups.map((g) => ({
      name: g.name,
      // 断言用「主体中线」midBody（依据见 inkMidline 第 3 条：撑不满外框 60% 就退回外框）：外框中线 midDev 会被细笔画
      // 单方面拖走，按外框居中就让带降部的单位骑在数字的 cap 线上。
      // 亚像素半峰插值（midSub）对非对称字形也会撒谎（↓↑ 箭头实测偏 5~9 设备px），只留 artifact 备查。
      // 小窗按主体中线判（用户看的就是这条）；设置窗不接定律、且它的标签是中英混排
      // （同一 run 里 Latin 顶比 CJK 低，主体行段会把它自己切开），沿用外框口径。
      dev: spreadsOf(g.items, mode === 'widget' ? 'midBody' : 'midDev')
    })),
    // 逐段散差同样只看共线人口（上标角色另有第 4 条），否则每个带温度的段都会把 `°` 报成离群者
    segs: Object.fromEntries(Object.entries(byGroup).map(([k, v]) => [
      k,
      spreadsOf(v.filter((r) => !SUPERSCRIPT.has(r.role)), mode === 'widget' ? 'midBody' : 'midDev')
    ])),
    runs
  };
}

app.commandLine.appendSwitch('disable-gpu'); // 与生产一致（main/index.ts）
app.disableHardwareAcceleration();

app.whenReady().then(async () => {
  nativeTheme.themeSource = 'dark';
  const win = new BrowserWindow({
    show: false,
    width: MODES.includes('settings') && !process.env.DSM_MODES ? 1000 : 900,
    height: 1400,
    x: 0,
    y: 0,
    backgroundColor: '#1d1d1f',
    transparent: false,
    frame: false,
    hasShadow: false,
    resizable: false,
    skipTaskbar: true,
    webPreferences: {
      preload: PRELOAD,
      nodeIntegration: true,
      contextIsolation: false,
      sandbox: false,
      backgroundThrottling: false
    }
  });

  // 设置窗字号不跟小窗滑杆（settings.css 固定 13px），扫 9 档是空转，测一次即可。
  const combos = [];
  for (const mode of MODES) {
    if (mode === 'widget') {
      // 小窗是判据主体：字号 × 状态 × 字体栈 三条轴全扫
      for (const font of FONTS) for (const size of SIZES) for (const state of STATES) {
        combos.push([mode, size, state, font]);
      }
    } else {
      // 设置窗字号固定 13px，滑杆不影响它；字体栈轴只跑第一项
      combos.push([mode, 12, 'n/a', FONTS[0]]);
    }
  }
  const comboSpread = (rep) =>
    Math.max(...rep.assert.map((g) => (g.dev ? g.dev.spread : 0)));
  const comboWorst = (rep) =>
    rep.assert
      .map((g) => ({ name: g.name, dev: g.dev }))
      .sort((a, b) => (b.dev ? b.dev.spread : 0) - (a.dev ? a.dev.spread : 0))[0];

  const reports = [];
  const broken = [];
  const lines = [];
  lines.push('instrument: capturePage 真像素判据  共线 tol=' + TOL + 'dev' +
    '  居中 tol=' + CENTER_TOL_DEV + 'dev（按 DPR 折算，下限 ' + CENTER_TOL + 'css）' +
    '  边距 ' + GAP_TARGET + '±' + GAP_TOL + 'css  上标 tol=' + SUP_TOL + 'dev  fonts=[' +
    FONTS.join(' ') + ']  (pad=' + PAD + ' inset=' + INSET + ')');
  let firstDetail = null;
  for (const [mode, size, state, font] of combos) {
    const tag =
      mode + ' fs=' + size + ' ' + state + (font && font !== 'auto' ? ' @' + font : '');
    let rep;
    try {
      rep = await measureOnce(win, mode, size, state, font);
    } catch (e) {
      broken.push(tag + ': ' + e.message);
      lines.push('BROKEN ' + tag + '  ' + e.message);
      continue;
    }
    reports.push(rep);
    const usable = rep.assert.filter((g) => g.dev);
    if (!usable.length) {
      broken.push(tag + ': 断言组内不足 2 个 run，判据无从比较');
      lines.push('BROKEN ' + tag + '  无可断言的组');
      continue;
    }
    const undetected = rep.runs.filter((r) => r.missing).length;
    if (undetected) {
      broken.push(tag + ': ' + undetected + ' 个 run 未检出墨迹');
      lines.push('BROKEN ' + tag + '  ' + undetected + ' 个 run 未检出墨迹');
    }
    if (rep.clip && rep.clip.length) {
      broken.push(tag + ': ' + rep.clip.length + ' 个 run 墨迹越出卡片内容盒（被裁） → ' + rep.clip.join(' / '));
      lines.push('CLIP   ' + tag + '  ' + rep.clip.join(' / '));
    }
    // 第 4 条：上标记号（`°`）的墨迹上沿离 cap 线有多远。它同时是一条**存在性**守卫——
    // 带温度的段就该有 `°`，一个都没检出说明角色映射断了（例如 Reading.vue 不再给 `°`
    // 挂 .unit--deg），那时这条判据会因为「没有对象」而静默变绿。静默变绿的判据等于没有判据。
    const sup = rep.superscript;
    if (mode === 'widget' && (SEGS.includes('cpu') || SEGS.includes('gpu')) && !sup) {
      broken.push(tag + ': 一个上标 run（`°`）都没检出，上标判据无从执行 → 角色映射或模板断了');
      lines.push('BROKEN ' + tag + '  上标 run 未检出');
    }
    const supErr = sup ? sup.maxAbsDev : null;
    const supBad = supErr !== null && supErr > SUP_TOL;
    if (supBad) {
      broken.push(tag + ': 上标墨迹上沿离 cap 线 ' + supErr.toFixed(2) + 'dev（允差 ' + SUP_TOL +
        '）→ ' + sup.worst);
    }
    const spread = comboSpread(rep);
    const w = comboWorst(rep);
    const over = usable.filter((g) => g.dev.spread > TOL).length;
    // 居中与边距只对拿得到卡片盒的组合成立（设置窗没有 .bar，自然不参与）。
    const c = rep.center;
    const centerErr = c ? c.unionErrCss : null;
    const mainErr = c ? c.mainErrCss : null;
    // 每组合各按自己的 sf 折算：换机器、换显示器缩放比时判据跟着走，不再需要人工记忆
    const centerTolCss = Math.max(CENTER_TOL, CENTER_TOL_DEV / rep.sf);
    const centerBad = !!c && Math.abs(centerErr) > centerTolCss;
    const mainBad = !!c && Math.abs(mainErr) > Math.max(MAIN_CENTER_TOL, centerTolCss);
    const gapErr = c
      ? Math.max(Math.abs(c.gapTopCss - GAP_TARGET), Math.abs(c.gapBottomCss - GAP_TARGET))
      : null;
    const gapBad = gapErr !== null && gapErr > GAP_TOL;
    if (centerBad) broken.push(tag + ': 墨迹并集中线离卡片中线 ' + centerErr.toFixed(2) +
      'css（允差 ' + centerTolCss.toFixed(2) + 'css = ' + (centerTolCss * rep.sf).toFixed(2) + 'dev）');
    if (mainBad) broken.push(tag + ': 主读数中线离卡片中线 ' + mainErr.toFixed(2) +
      'css（允差 ' + centerTolCss.toFixed(2) + 'css = ' + (centerTolCss * rep.sf).toFixed(2) + 'dev）');
    if (gapBad) broken.push(tag + ': 墨迹上下沿到可视边框 ' + c.gapTopCss.toFixed(2) + '/' +
      c.gapBottomCss.toFixed(2) + 'css，目标 ' + GAP_TARGET + '±' + GAP_TOL + '（该组合应给卡高 ' + c.wantCardHCss + '）');
    lines.push(
      (spread > TOL || centerBad || mainBad || gapBad || supBad ? 'RED  ' : 'ok   ') +
      mode.padEnd(8) + ' fs=' + String(size).padEnd(3) + ' ' + state.padEnd(7) +
      String(font || 'auto').slice(0, 13).padEnd(14) +
      ' dpr=' + rep.dpr +
      '  散差 ' + spread.toFixed(2).padStart(5) + 'dev (' + (spread / rep.sf).toFixed(2).padStart(5) + 'css)' +
      '  居中 ' + (centerErr === null ? ' --  ' : centerErr.toFixed(2).padStart(5)) +
      '/' + (mainErr === null ? ' --  ' : mainErr.toFixed(2).padStart(5)) + 'css' +
      '  边距 ' + (c ? c.gapTopCss.toFixed(2).padStart(5) + '/' + c.gapBottomCss.toFixed(2).padEnd(5) : ' --      --    ') +
      (gapErr === null ? '' : 'css 偏 ' + gapErr.toFixed(2).padStart(5)) +
      '  上标 ' + (supErr === null ? ' --  ' : supErr.toFixed(2).padStart(5) + 'dev') +
      '  应给高 ' + (c ? String(c.wantCardHCss).padStart(5) : ' --  ') +
      '  超标组 ' + over + '/' + usable.length +
      '  最差组 ' + String(w.name).padEnd(12) +
      ' 中线差 ' + w.dev.spread.toFixed(2).padStart(4) + 'dev  离群 ' + String(w.dev.worst).slice(0, 28).padEnd(28) +
      ' 偏 ' + w.dev.worstDev.toFixed(2) + 'dev'
    );
    if (size === 12 && state === 'values' && (font || 'auto') === 'auto' && !firstDetail) firstDetail = rep;
  }

  if (firstDetail) {
    lines.push('', 'detail fs=12 ' + firstDetail.mode + '（每 run 的墨迹上下沿与中线，设备像素）：');
    // Δ 都相对该组合的第一个有效 run，便于直接读出「扇形」的每一根辐条
    const r0 = firstDetail.runs.find((x) => !x.missing) || { midSubCss: 0, midSub: 0 };
    const baseCss = r0.midSubCss;
    for (const r of firstDetail.runs) {
      lines.push(r.missing
        ? '  ' + r.key.padEnd(34) + ' 带内未检出墨迹'
        : '  ' + r.key.padEnd(30) + ' ink=[' + String(r.top).padStart(4) + ',' + String(r.bottom).padStart(4) +
          '] h=' + String(r.inkHeightDev).padStart(2) + ' cov=' + String(r.peakCov).padStart(5) +
          '  mid 行盒=' + String(r.midDev).padStart(7) + ' 亚像素=' + String(r.midSub).padStart(7) +
          '  Δcss=' + (r.midSubCss - baseCss).toFixed(2).padStart(7) + '  Δdev=' + (r.midSub - r0.midSub).toFixed(2).padStart(6) +
          // 上标角色另有参照线，另报「我的上沿离 cap 线多远」，0 = 正坐在 cap 线上
          (SUPERSCRIPT.has(r.role) && firstDetail.superscript
            ? '  离cap=' + (r.top - firstDetail.superscript.capTopDev).toFixed(2) + 'dev'
            : ''));
    }
  }

  // 趋势判据：散差对字号做最小二乘，斜率要与「它自己的噪声」比，而不是与我拍的常数比。
  // 自然中线落在 0.5 设备px 的格点上 ⇒ 每档读数带 ±0.25 抖动，九档拟合出的斜率本身
  // 就有非零期望。SE(斜率)=σ_残差/√Σ(x−x̄)²，判据取 slope > 3·SE，再兜 0.02 下限，
  // 防「完美直线」把门无限收紧。参照：HEAD 的 slope=+0.20 是单调张开、残差小 ⇒ 红；
  // 本轮 slope=+0.083 与 3·SE 同量级 ⇒ 判为与格点抖动不可区分，这才是对的结论。
  const trendLines = [];
  let trendFail = false;
  // 三条轴的分组键：字体栈 × 状态。趋势要逐栈看——某一族字体单独张开正是「换个字体就失效」的形状。
  const groups = [...new Set(reports.map((r) => (r.font || 'auto') + '\u0000' + r.state))];
  for (const key of groups) {
    const [font, st] = key.split('\u0000');
    const pts = reports
      .filter((r) => r.mode === 'widget' && r.state === st && (r.font || 'auto') === font)
      .map((r) => [r.size, comboSpread(r)]);
    if (pts.length < 4) continue;
    const mx = pts.reduce((a, p) => a + p[0], 0) / pts.length;
    const my = pts.reduce((a, p) => a + p[1], 0) / pts.length;
    const sxx = pts.reduce((a, p) => a + (p[0] - mx) * (p[0] - mx), 0);
    const slope = pts.reduce((a, p) => a + (p[0] - mx) * (p[1] - my), 0) / sxx;
    const resid = pts.map((p) => p[1] - (my + slope * (p[0] - mx)));
    const sigma = Math.sqrt(resid.reduce((a, r) => a + r * r, 0) / (pts.length - 2));
    const lim = Math.max((3 * sigma) / Math.sqrt(sxx), 0.02);
    const bad = slope > lim;
    trendFail = trendFail || bad;
    trendLines.push('  趋势 ' + String(st).padEnd(7) + ' ' + String(font).padEnd(16) +
      ' 斜率 ' + slope.toFixed(3) + '  3·SE 门限 ' + lim.toFixed(3) +
      '（σ_残差 ' + sigma.toFixed(2) + '）' + (bad ? '  RED 扇形随字号张开' : '  ok 与格点抖动不可区分'));
  }
  // 逐字体栈汇总：换一族字体，四条判据各漂多远一眼可见
  const fontLines = [];
  for (const font of [...new Set(reports.map((r) => r.font || 'auto'))]) {
    const rs = reports.filter((r) => (r.font || 'auto') === font && r.mode === 'widget');
    if (!rs.length) continue;
    const abs = (v) => (v == null ? 0 : Math.abs(v));
    const cent = rs.filter((r) => r.center);
    const cMax = Math.max(...cent.map((r) => abs(r.center.unionErrCss)));
    const mMax = Math.max(...cent.map((r) => abs(r.center.mainErrCss)));
    const gMax = Math.max(...cent.map((r) => Math.abs(r.center.gapTopCss - GAP_TARGET) >
      Math.abs(r.center.gapBottomCss - GAP_TARGET)
      ? Math.abs(r.center.gapTopCss - GAP_TARGET) : Math.abs(r.center.gapBottomCss - GAP_TARGET)));
    const sMax = Math.max(...rs.map((r) => (r.superscript ? r.superscript.maxAbsDev : 0)));
    fontLines.push('  字体栈 ' + String(font).padEnd(18) + ' 组合 ' + String(rs.length).padStart(3) +
      '  散差最大 ' + Math.max(...rs.map(comboSpread)).toFixed(2).padStart(5) + 'dev' +
      '  居中最大 ' + cMax.toFixed(2).padStart(5) + 'css' +
      '  主读数最大 ' + mMax.toFixed(2).padStart(5) + 'css' +
      '  边距偏差最大 ' + gMax.toFixed(2).padStart(5) + 'css' +
      '  上标偏差最大 ' + sMax.toFixed(2).padStart(5) + 'dev');
  }
  const fails = reports.filter((r) =>
    comboSpread(r) > TOL ||
    (r.center && (Math.abs(r.center.unionErrCss) > Math.max(CENTER_TOL, CENTER_TOL_DEV / r.sf) ||
      Math.abs(r.center.mainErrCss) > Math.max(MAIN_CENTER_TOL, CENTER_TOL, CENTER_TOL_DEV / r.sf) ||
      Math.abs(r.center.gapTopCss - GAP_TARGET) > GAP_TOL ||
      Math.abs(r.center.gapBottomCss - GAP_TARGET) > GAP_TOL)) ||
    (r.superscript && r.superscript.maxAbsDev > SUP_TOL));
  const worst = reports.reduce((a, r) => (comboSpread(r) > (a ? comboSpread(a) : -1) ? r : a), null);
  lines.push(
    '',
    ...fontLines,
    ...trendLines,
    'VERDICT ' + (fails.length || broken.length || trendFail ? 'RED' : 'GREEN') +
      '  combos=' + reports.length + '  超标=' + fails.length + '  BROKEN=' + broken.length +
      (worst ? '  worst=' + worst.mode + '/fs' + worst.size + '/' + worst.state + ' ' + comboSpread(worst).toFixed(2) + 'dev' : ''),
    '判据：共线散差 ≤' + TOL + 'dev；整条 bar 居中误差 ≤' + CENTER_TOL_DEV +
    'dev（按 DPR 折算成 CSS px，下限 ' + CENTER_TOL + 'css）；' +
      '最大字样到可视边框 ' + GAP_TARGET + '±' + GAP_TOL + 'css；' +
      '上标墨迹上沿离 cap 线 ≤' + SUP_TOL + 'dev；裁切即 BROKEN'
  );
  if (broken.length) lines.push('BROKEN 明细：', ...broken.map((b) => '  ' + b));
  fs.writeFileSync(REPORT, JSON.stringify(reports, null, 1));
  lines.push('artifact=' + REPORT);
  console.log(lines.join('\n'));
  app.exit(fails.length || broken.length ? 1 : 0);
}).catch((err) => {
  // 量具自身出错必须以非零码退出：静默崩溃会被读成「没报错＝通过」
  console.log('INSTRUMENT CRASH ' + (err && err.stack ? err.stack : err));
  app.exit(2);
});
