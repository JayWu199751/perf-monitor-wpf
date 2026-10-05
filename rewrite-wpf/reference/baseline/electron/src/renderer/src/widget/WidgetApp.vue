<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import type { MetricsSnapshot, Settings } from '@shared/types';
import { formatLocalTime } from '../lib/time';
import { createSizeReporter } from '../lib/cardSize';
import MetricSegment from './components/MetricSegment.vue';
import Reading from './components/Reading.vue';
import NetReadings from './components/NetReadings.vue';

type SegmentKey = 'cpu' | 'mem' | 'gpu' | 'net' | 'time';
// 段序与分隔线顺序的唯一来源：可见段之间插发丝线，末段后不插
const ORDER: SegmentKey[] = ['cpu', 'mem', 'gpu', 'net', 'time'];

const snapshot = ref<MetricsSnapshot | null>(null);
const settings = ref<Settings | null>(null);
const clock = ref(formatLocalTime(new Date()));
const barEl = ref<HTMLElement | null>(null);

// 设置未到达前保持全量槽位：先按最宽形态占位，避免窗口宽度来回抖
const visible = computed<SegmentKey[]>(() => {
  const m = settings.value?.metrics;
  if (!m) return ORDER;
  return ORDER.filter((key) => m[key]);
});

const barStyle = computed(() => {
  const s = settings.value;
  // --fs 是这一版唯一的用户量：所有字号阶梯与光学校正都由它推出来，
  // 不再各自写死（老代码把 fontSize 直接写进 font-size，校正常数却按 12px 定）。
  // 卡片高度不在这里给：它由 .bar 的 text-box 裁到最大字号的墨迹外框 + 上下 --edge-gap
  // 派生出来（见下方样式与 ADR-0006 修正三），写死任何一个数都会让「最大字样到边框
  // 的距离」重新随字号漂移。
  return {
    backgroundColor: s?.transparentDisplay
      ? 'transparent'
      : `rgba(var(--bar-bg-rgb), ${s?.opacity ?? 0.72})`,
    '--fs': `${s?.fontSize ?? 12}px`
  };
});

// 卡片的真实尺寸只能问布局：宽 = 内容宽，高 = 裁到墨迹外框后再加上下边距的结果。
// 两者一起上报给主进程，贴边推出偏移与任务栏行内判定都吃这个数（ADR-0003）。
// 「宽度只增不减、高度可以双向、什么时候不值得再报一次」这三条规则在 lib/cardSize.ts，
// 那里有测试；这里只留「量谁、什么时候量」。
const reporter = createSizeReporter((size) => window.api.resizeWidget(size.width, size.height));

// 设置/指标/布局变化都走 reporter 的同一个入口，调用方不选择是否允许缩窄。
function measure(): void {
  const el = barEl.value;
  if (!el) return;
  reporter.update(el, settings.value);
}

let clockTimer: ReturnType<typeof setInterval> | null = null;

// 时间在小窗内独立每秒更新，不占主进程采样轮次
watch(
  () => settings.value?.metrics.time ?? true,
  (on) => {
    if (!on) {
      if (clockTimer !== null) {
        clearInterval(clockTimer);
        clockTimer = null;
      }
      return;
    }
    clock.value = formatLocalTime(new Date());
    if (clockTimer === null) {
      clockTimer = setInterval(() => {
        clock.value = formatLocalTime(new Date());
      }, 1000);
    }
  },
  { immediate: true }
);

// 卡片尺寸变化（数字位数、字号、DPI 缩放）统一由 ResizeObserver 捕获，
// 比逐个 matchMedia 监听分辨率档位可靠；主进程据此重算贴边推出偏移。
let observer: ResizeObserver | null = null;
const unsubs: Array<() => void> = [];

onMounted(() => {
  if (barEl.value && typeof ResizeObserver !== 'undefined') {
    observer = new ResizeObserver(() => measure());
    observer.observe(barEl.value);
  }
  unsubs.push(window.api.onSettings((s) => (settings.value = s)));
  unsubs.push(window.api.onMetrics((s) => (snapshot.value = s)));
  void window.api.getSettings().then((s) => (settings.value = s));
});

onBeforeUnmount(() => {
  observer?.disconnect();
  observer = null;
  if (clockTimer !== null) clearInterval(clockTimer);
  unsubs.forEach((off) => off());
  unsubs.length = 0;
});

watch(settings, () => void nextTick(measure));
watch(snapshot, () => void nextTick(measure));
</script>

<template>
  <div
    ref="barEl"
    class="bar"
    :class="{ 'bar--transparent-display': settings?.transparentDisplay }"
    role="group"
    aria-label="系统性能"
    :style="barStyle"
  >
    <template v-for="(key, i) in visible" :key="key">
      <span v-if="i > 0" class="sep" aria-hidden="true"></span>
      <MetricSegment v-if="key === 'cpu'" label="CPU" title="CPU 使用率 / 温度">
        <Reading :value="snapshot?.cpuPct ?? null" unit="%" />
        <Reading :value="snapshot?.cpuTemp ?? null" unit="°" tone="secondary" />
      </MetricSegment>
      <MetricSegment v-else-if="key === 'mem'" label="内存" title="内存占用">
        <Reading :value="snapshot?.memPct ?? null" unit="%" />
      </MetricSegment>
      <MetricSegment v-else-if="key === 'gpu'" label="GPU" title="GPU 使用率 / 显存占用 / 温度">
        <Reading :value="snapshot?.gpuPct ?? null" unit="%" />
        <Reading :value="snapshot?.gpuMemPct ?? null" unit="%" tone="secondary" />
        <Reading :value="snapshot?.gpuTemp ?? null" unit="°" tone="secondary" />
      </MetricSegment>
      <MetricSegment v-else-if="key === 'net'" label="网络" title="下载 / 上传速率" class="seg-net">
        <NetReadings :down="snapshot?.netDownMBs ?? null" :up="snapshot?.netUpMBs ?? null" />
      </MetricSegment>
      <MetricSegment v-else title="本地时间">
        <span class="clock optical">{{ clock }}</span>
      </MetricSegment>
    </template>
  </div>
</template>

<style scoped>
.bar {
  display: block;
  /* 卡片高度 = 最大字号那批 run 的墨迹外框 + 上下各一个 --edge-gap。
     text-box 把内容盒的上下沿直接裁到「cap 顶 → 基线」，用的是本元素此刻真正解析出来的
     那族字体的度量，所以这条距离与字号、与 DPI、与换了哪族字体都无关——旧写法是把卡高
     钉成常数（那一版目标是 4px），字号一大墨迹就顶到边框，实测同一格掉到 3.4px。
     裁切与上下对称是同一件事：内容盒被裁成墨迹外框后，上下等大的 padding 天然把墨迹
     摆在正中间。 */
  height: auto;
  padding: calc(var(--edge-gap) + var(--edge-bleed)) 8px;
  color: var(--bar-text);
  font-size: var(--fs, 12px);
  text-box: trim-both cap alphabetic;
  /* --lh-widget = 20px 是统一行盒。它与卡高无关（卡高由上面的 text-box 派生，随字号呼吸），
     只决定那条共享基线落在设备网格的哪一格。
     这里刻意是 block 而不是 flex：整条 bar 要落在同一个 inline 格式化上下文里，只有一条
     基线、只被设备网格吸附一次，optical.css 的两项定律才能把墨迹中线收到卡片中心线上。
     一旦改回 inline-flex 排文字，25 个 run 各自取整盒中心，共线极限退回 1 设备px
     （tests/opticalCenter.test.ts 钉住这条形状）。*/
  line-height: var(--lh-widget);
  letter-spacing: 0;
  white-space: nowrap;
  background-clip: padding-box;
  border: 1px solid transparent;
  /* 圆角随字号呼吸（卡高本身已经随字号走），否则 fs10 时近胶囊、fs18 时已是一块方角矩形。 */
  border-radius: calc(var(--fs, 12px) * 0.8);
  box-shadow: inset 0 0 0 1px var(--bar-border);
  backdrop-filter: blur(18px) saturate(140%);
  -webkit-backdrop-filter: blur(18px) saturate(140%);
  /* 整卡可拖：主进程用 system-context-menu 拦掉 drag 区右键的系统菜单，改弹托盘同款菜单 */
  -webkit-app-region: drag;
  user-select: none;
  font-variant-numeric: tabular-nums;
  font-feature-settings: 'tnum' 1;
}

.sep {
  display: inline-block;
  /* 分隔线自己不能参与行盒：卡片高度现在只由文字墨迹派生，所以这里给一个 0 高、
     正好落在共享基线上的点，发丝线用绝对定位画出来，既不撑高卡片也不会被裁。
     纵向长度与位置都取 1cap = 「最大的字样」的墨迹高，随字号一起呼吸；
     横向 0.83×--fs（≈fs12 的 10px）让两侧各留约 5px 空气，比段内最大的标签间距还高一级，
     段界因此读得出来；同样随 --fs 走。 */
  width: calc(var(--fs, 12px) * 0.83);
  height: 0;
  vertical-align: 0;
  position: relative;
}

.sep::before {
  content: '';
  position: absolute;
  left: 50%;
  top: calc(-0.5cap); /* 墨迹外框的正中 = 基线上方半个 cap */
  width: 1px;
  height: 1cap;
  transform: translate(-50%, -50%);
  background: var(--bar-divider);
}

.clock {
  --a: var(--a-text);
  --rho: var(--rho-text);
  font-variant-numeric: tabular-nums;
  line-height: var(--lh-widget);
}

@media (min-resolution: 1.5dppx) {
  .bar {
    box-shadow: inset 0 0 0 0.5px var(--bar-border);
  }
}

/* 透明显示只移除卡片装饰，不改指标文字和拖动/右键命中区域。 */
.bar--transparent-display {
  box-shadow: none;
  backdrop-filter: none;
  -webkit-backdrop-filter: none;
}

.bar--transparent-display .sep::before {
  visibility: hidden;
}
</style>
