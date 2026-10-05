import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// 这条定律的几何判据在像素那边（npm run measure:alignment）——渲染层没有纯函数接口，
// 抬量最终由 Chromium 的光栅器决定，任何「算一遍中线」的单测都是自证。
// 本文件只守确定能守的事：**别再让老 bug 的那些形状回来**。
// 老 bug 的形状是 11 个「整 1 设备px @12px @175%」的快照散在 4 个文件里，换字号即张开到
// 3.5 设备px；再往后一版的形状是「每个 run 各自一个盒中心」，25 次独立取整把共线极限
// 钉死在 1 设备px。所以这里把两代的形状都钉住。
const ROOT = resolve(__dirname, '..');
const OPTICAL = readFileSync(resolve(ROOT, 'src/renderer/src/styles/optical.css'), 'utf8');
// 小窗这一侧的全部渲染文件，外加「量布局尺寸 → 决定报什么」那条规则所在的地方：
// 形状守卫要扫的是这条链路整体，不然规则挪个家就等于把守卫关掉。
const WIDGET_FILES = [
  'src/renderer/src/widget/WidgetApp.vue',
  'src/renderer/src/widget/components/MetricSegment.vue',
  'src/renderer/src/widget/components/Reading.vue',
  'src/renderer/src/widget/components/NetReadings.vue',
  'src/renderer/src/lib/cardSize.ts'
].map((f) => ({ f, src: readFileSync(resolve(ROOT, f), 'utf8') }));
const allStyles = [OPTICAL, ...WIDGET_FILES.map((x) => x.src)].join('\n');
const MEASURE_SRC = readFileSync(resolve(ROOT, 'scripts/alignment/measure.cjs'), 'utf8');
const FIT_SRC = readFileSync(resolve(ROOT, 'scripts/alignment/fit-rho.cjs'), 'utf8');

/** 角色名单的唯一出处（量具与反解器 require 的是同一个文件）。 */
async function loadRoles(): Promise<{ SUPERSCRIPT: Set<string>; roleOf: (k: string) => string }> {
  const mod = (await import('../scripts/alignment/roles.cjs')) as { default?: unknown };
  const m = (mod.default ?? mod) as { SUPERSCRIPT: Set<string>; roleOf: (k: string) => string };
  return m;
}

/** 从 optical.css 读出 a 表与 ρ 表 */
function table(css: string, prefix: string): Record<string, number> {
  const out: Record<string, number> = {};
  for (const m of css.matchAll(new RegExp(`--${prefix}-([a-z-]+):\\s*(-?[\\d.]+)\\s*;`, 'g'))) {
    out[m[1]] = Number(m[2]);
  }
  return out;
}

describe('文字垂直居中的光学校正', () => {
  const rho = table(OPTICAL, 'rho');
  const a = table(OPTICAL, 'a');

  // CSS 没有 // 行注释：写了会让那一条声明整条作废。踩过的真实一次是
  // `--edge-gap: 8px; // 说明` —— 变量变空 → .bar 的 padding calc() 失效 → 卡片掉到 0 内边距，
  // 而源码正则类断言全都还是绿的，只有像素判据会红。这条把它挡在单测里。
  it('optical.css 里不许出现 // 注释（CSS 无此语法，会让整条声明失效）', () => {
    // 先把合法的 /* */ 块整体摘掉再找，否则注释里提到这个写法也会自己打自己
    const code = OPTICAL.replace(/\/\*[\s\S]*?\*\//g, '');
    const bad = code
      .split('\n')
      .map((l, i) => ({ l, n: i + 1 }))
      .filter(({ l }) => l.includes('//'));
    expect(bad.map(({ n, l }) => `${n}: ${l.trim()}`).join('\n'), 'CSS 里出现 // 注释').toBe('');
  });

  it('两张表都是无量纲常数，且条目一一对应', () => {
    expect(Object.keys(rho).length).toBeGreaterThanOrEqual(5);
    for (const [k, v] of Object.entries(rho)) {
      expect(Number.isFinite(v), k).toBe(true);
      // 手调成像素值的第一征兆就是某一项的量级像 px：任一项 ≥1 即判错（1 设备px 在本机是
      // 1.75 CSS px，一个被当成 px 写进来的数必然越过这条线）。
      // 口径从旧版的「|a|+|ρ| < 1.2」改成「逐项 < 1」：`°` 的参照线是 cap 线而不是中心线
      // （roles.cjs + ADR-0006 修正五），它的两项天然部分相消，和值 1.43 却每一项都 < 1。
      // 真正要挡的是「这一项像不像像素」，不是「两项加起来大不大」。
      expect(Math.abs(v), k + ' 的 ρ 项量级像像素值').toBeLessThan(1);
      expect(Math.abs(a[k] ?? 0), k + ' 的 a 项量级像像素值').toBeLessThan(1);
    }
    for (const k of Object.keys(rho)) expect(a[k], 'a-' + k + ' 缺条目').toBeDefined();
    expect(/--rho-[a-z-]+:\s*-?[\d.]+(px|em|rem)/.test(OPTICAL), '表里出现了带单位的常数').toBe(false);
    expect(/--a-[a-z-]+:\s*-?[\d.]+(px|em|rem)/.test(OPTICAL), '表里出现了带单位的常数').toBe(false);
  });

  it('定律只在一处执行：带长度的 vertical-align 只允许出现在 optical.css 的 .optical', () => {
    const hits = allStyles.match(/vertical-align:\s*calc\([^;]*\)/g) ?? [];
    expect(hits.length, 'vertical-align: calc 的声明处数：' + JSON.stringify(hits)).toBe(1);
    expect(OPTICAL).toContain('var(--a');
    expect(OPTICAL).toContain('var(--rho');
    // 定律里出现「数字+单位」就是手调常数复活。先把 var(...) 整体剥掉（兜底值里合法地
    // 带 px），再把定律自己的参照单位 1em 剥掉，剩下的任何长度字面量都算违规。
    const law = (hits[0] ?? '')
      .replace(/var\([^()]*\)/g, 'var()')
      .replace(/var\([^()]*\)/g, 'var()')
      .replace(/\b1em\b/g, '');
    expect(law).not.toMatch(/[-\d.]+(px|em|rem|vh|vw)/);
  });

  it('老机制不许回来：渲染层里没有任何 translateY，也没有第二套参照', () => {
    const bad = allStyles.match(/transform:[^;]*translateY/g) ?? [];
    expect(bad, 'translateY 又出现了：' + JSON.stringify(bad)).toEqual([]);
    const bare = allStyles.match(/vertical-align:\s*[-\d.]+(px|em|rem)/g) ?? [];
    expect(bare, 'vertical-align 用了裸长度值：' + JSON.stringify(bare)).toEqual([]);
  });

  it('整条 bar 必须是一个共享基线的 inline 流：容器不许再用 flex 排文字', () => {
    // 每个文字 run 一旦重新变成独立 flex 项，盒中心就各自取整，共线极限退回 1 设备px
    for (const { f, src } of WIDGET_FILES) {
      for (const cls of ['bar', 'seg', 'val', 'reading']) {
        const m = src.match(new RegExp('\\.' + cls + '\\s*\\{([^}]*)\\}', 'g')) ?? [];
        for (const block of m) {
          expect(block, f + ' 的 .' + cls + ' 又用 flex 排文字了').not.toMatch(/display:\s*inline-flex/);
        }
      }
    }
  });

  it('每个会渲染文字的 run 都同时认领了 a 与 ρ（漏一个就会自己漂到别的格点）', () => {
    const claiming = new Set([...allStyles.matchAll(/--rho:\s*var\(--rho-([a-z-]+)\)/g)].map((m) => m[1]));
    const claimingA = new Set([...allStyles.matchAll(/--a:\s*var\(--a-([a-z-]+)\)/g)].map((m) => m[1]));
    for (const role of Object.keys(rho)) {
      expect(claiming.has(role), 'rho-' + role + ' 定义了却没人认领').toBe(true);
      expect(claimingA.has(role), 'a-' + role + ' 定义了却没人认领').toBe(true);
    }
    // 模板里带文字的 run 必须挂 .optical，否则 --rho 只是个没人用的变量。
    // 匹配前先把 :class="..." 动态绑定整条剥掉：否则
    // :class="{ 'seg--time': !label }" 会被当成一个带 label 的 class 属性。
    const seen = new Set<string>();
    for (const { f, src } of WIDGET_FILES) {
      const tpl = src
        .slice(src.indexOf('<template>'), src.indexOf('</template>'))
        .replace(/:class="[^"]*"/g, '');
      for (const m of tpl.matchAll(/class="([^"]*)"/g)) {
        for (const cls of ['label', 'digits', 'unit', 'slot', 'clock']) {
          if (!m[1].split(/\s+/).includes(cls)) continue;
          seen.add(cls);
          expect(m[1], f + ' 里的 .' + cls + ' 缺 .optical').toContain('optical');
        }
      }
    }
    for (const cls of ['label', 'digits', 'unit', 'slot', 'clock']) {
      expect(seen.has(cls), '整个小窗里找不到 .' + cls + ' 这个文字 run').toBe(true);
    }
  });

  it('字号只有一个入口：卡片字号必须由 --fs 派生（防「某一档凑数」）', () => {
    const app = WIDGET_FILES[0].src;
    expect(app).toMatch(/--fs/);
    expect(app).toMatch(/font-size:\s*var\(--fs/);
    expect(app).not.toMatch(/fontSize:\s*\D*\$\{[^}]*\}px/);
  });

  // 卡片高度的形状约束。旧版是「卡高 = 常数，--lh-widget = 卡高 − 2」这条等式，
  // 它保证不了「最大的字样到可视边框的距离」随字号恒定（实测 6.6px 掉到 3.4px），
  // 所以卡高改成由墨迹派生；这里钉住新形状，别让旧形状悄悄回来。
  it('最大字样边距只有一个源头：optical.css 的 --edge-gap 与 shared 的 WIDGET_EDGE_GAP 同源', async () => {
    const { WIDGET_EDGE_GAP } = await import('../src/shared/types');
    const m = OPTICAL.match(/--edge-gap:\s*(\d+(?:\.\d+)?)px/);
    expect(m, 'optical.css 里没有 --edge-gap 的像素值').not.toBeNull();
    expect(Number(m![1])).toBe(WIDGET_EDGE_GAP);
  });

  it('卡高必须由墨迹派生：.bar 是 auto 高 + text-box 裁到 cap→基线 + 上下等大的边距', () => {
    const app = WIDGET_FILES[0].src;
    const bar = app.match(/\.bar\s*\{([^}]*)\}/);
    expect(bar, 'WidgetApp.vue 里找不到 .bar 的规则块').not.toBeNull();
    expect(bar![1]).toMatch(/height:\s*auto/);
    expect(bar![1], '不裁到墨迹，卡高就还是随字号漂').toMatch(/text-box:\s*trim-both\s+cap\s+alphabetic/);
    expect(bar![1], '上下边距必须同源且对称，否则居中又要靠手调常数').toMatch(
      /padding:\s*calc\(var\(--edge-gap\)\s*\+\s*var\(--edge-bleed\)\)\s+[\d.]+px/
    );
    // 把卡高写回内联样式 = 回到「钉死一个数」那一版
    expect(app, '卡片高度不许再由内联样式下发').not.toMatch(/height:\s*`?\$\{/);
  });

  it('渲染端不许再引用 WIDGET_CARD_HEIGHT：上报的高度只能是量出来的', () => {
    for (const { f, src } of WIDGET_FILES) {
      expect(src, f + ' 里不该再有 WIDGET_CARD_HEIGHT').not.toMatch(/WIDGET_CARD_HEIGHT/);
    }
    // 量在哪、报在哪，两件事都要在：WidgetApp 负责「量谁」，lib/cardSize.ts 负责「量到什么」
    const [app, cardSize] = [WIDGET_FILES[0].src, WIDGET_FILES[4].src];
    expect(app, 'WidgetApp 要把布局量交给 cardSize 再上报').toMatch(/resizeWidget/);
    expect(cardSize, 'resizeWidget 的高度必须来自布局实测').toMatch(/offsetHeight/);
    // 高度不许被重新钉成常数：卡高随字号派生（见上一条与 ADR-0006 修正三）
    expect(cardSize, 'cardSize 不许给高度引入常量').not.toMatch(/height:\s*[A-Z_]{4,}\s*[,}]/);
  });

  it('统一行盒仍是无量纲的行盒高（它只决定基线落在设备网格哪一格）', () => {
    const m = OPTICAL.match(/--lh-widget:\s*(\d+(?:\.\d+)?)px/);
    expect(m, 'optical.css 里没有 --lh-widget 的像素值').not.toBeNull();
    // 卡高不再等于它 + 2 边框，所以这里只钉它仍是一个纯长度，别混进 calc(卡高…)
    expect(m![1]).not.toMatch(/calc/);
  });

  // 上标角色（`°`）不吃中心线、改吃 cap 线。这件事要说同一句话的地方有三处：
  // roles.cjs 的名单、optical.css 的那一对系数、量具的第 4 条判据。任何一处悄悄改回去，
  // 屏幕上复发的就是用户报的那个症状，所以三处一起钉。
  it('上标名单只有一个出处，量具与反解器都从 roles.cjs 取', async () => {
    const { SUPERSCRIPT, roleOf } = await loadRoles();
    expect([...SUPERSCRIPT], '上标名单变了要同步改判据').toEqual(['deg']);
    // 分类本身也钉住：认错了角色 = 判据静默换了对象
    expect(roleOf('CPU 2/unit "\u00b0"')).toBe('deg');
    expect(roleOf('CPU 1/unit "%"')).toBe('pct');
    expect(roleOf('网络 1/unit "MB/s"')).toBe('mbs');
    expect(roleOf('CPU 1/num "--"')).toBe('dash');
    expect(roleOf('CPU 1/num "45"')).toBe('text');
    expect(roleOf('网络 1/arrow "\u2193"')).toBe('arrow');
    expect(roleOf('CPU/label "CPU"')).toBe('label');
    for (const [name, src] of [
      ['measure.cjs', MEASURE_SRC],
      ['fit-rho.cjs', FIT_SRC]
    ] as const) {
      expect(src, name + ' 没有 require roles.cjs').toMatch(/require\('\.\/roles\.cjs'\)/);
      expect(src, name + ' 自己又写了一份角色分类').not.toMatch(/function roleOf\(/);
    }
  });

  it('上标角色有自己的系数，模板还在把 ° 认给它，量具还守着它的存在性', async () => {
    const { SUPERSCRIPT } = await loadRoles();
    for (const role of SUPERSCRIPT) {
      expect(rho[role], '上标角色 ' + role + ' 缺 ρ 项').toBeDefined();
      expect(a[role], '上标角色 ' + role + ' 缺 a 项').toBeDefined();
    }
    const reading = WIDGET_FILES.find((x) => x.f.endsWith('Reading.vue'))?.src ?? '';
    expect(reading, '° 不再走 .unit--deg，等于把它悄悄送回共线判据').toMatch(
      /'unit--deg':\s*unit === '\u00b0'/
    );
    // 「一个上标 run 都没检出即 BROKEN」这条守卫不许删：判据因为「没有对象」而变绿，
    // 比压根没有这条判据更糟。
    expect(MEASURE_SRC, '上标判据的存在性守卫没了').toMatch(/都没检出，上标判据无从执行/);
  });
});
