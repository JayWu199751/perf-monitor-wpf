<script setup lang="ts">
import { computed } from 'vue';
import { slotText } from '../../lib/netSlot';

// 一个数值槽位：数字 + 单位。缺失态（null）显示 `--` 并按其墨迹归位。
// 默认插槽放在数字前，供网速段插入方向箭头，使箭头与数字同处一个盒内。
// 垂直归位不在这里做：每个文字 run 各自认领一对 --a-*/--rho-*，由 optical.css 里唯一的
// .optical 定律（vertical-align）抬到卡片中心线上。整条 bar 是一个共享基线的 inline 流，
// 所以 .num 刻意留 inline-block——宽度由内容给，基线仍是那条共享基线。
// 2026-09-30 拿掉定宽槽：右对齐把槽里那截余量留在标签与数字之间，读数一到 100%（网速一到
// 10.0 MB/s）就把「标签↔首读数」的间距从 10px 吃到 3px——正是用户最想看清的那一档。
// 现在宽度由内容给，标签那侧吃固定 margin；代价与实测见 ADR-0006 修正七。
const props = withDefaults(
  defineProps<{
    value: number | null;
    unit: string;
    digits?: number;
    tone?: 'primary' | 'secondary';
  }>(),
  { digits: 0, tone: 'primary' }
);

const text = computed(() => slotText(props.value, props.digits));
const missing = computed(() => props.value == null);
</script>

<template>
  <span class="reading" :class="[`reading--${tone}`, { 'is-missing': missing }]"
    ><span class="num"><span class="slot optical"><slot /></span><span class="digits optical">{{ text }}</span></span
    ><span
      class="unit optical"
      :class="{ 'unit--deg': unit === '°', 'unit--mbs': unit === 'MB/s', 'unit--pct': unit === '%' }"
      >{{ unit }}</span
    ></span>
</template>

<style scoped>
.reading {
  line-height: var(--lh-widget);
  color: var(--bar-text);
  font-weight: 600;
}

.reading + .reading {
  /* 同段两个读数（主 + 伴随）之间：比「数字↔单位」的 1px 松、比标签那侧紧，
     与跨段的分隔线一起构成 1 / 0.5fs / 0.58fs / 0.83fs 的四级节奏，全随 --fs 呼吸。 */
  margin-left: calc(var(--fs, 12px) * 0.5);
}

.reading--secondary {
  color: var(--bar-secondary);
  font-size: clamp(9px, 0.9em, 14px);
  font-weight: 500;
}

.num {
  display: inline-block;
  line-height: var(--lh-widget);
}

/* 无前置内容（非网速段）时不占位，避免多一个空 flex 项 */
.slot:empty {
  display: none;
}

.slot {
  --a: var(--a-arrow);
  --rho: var(--rho-arrow);
  line-height: var(--lh-widget);
  font-weight: 700;
}

.digits {
  --a: var(--a-text);
  --rho: var(--rho-text);
  line-height: var(--lh-widget);
}

.unit {
  --a: var(--a-label);
  --rho: var(--rho-label);
  line-height: var(--lh-widget);
  color: var(--bar-secondary);
  font-size: clamp(8px, 0.82em, 12px);
  font-weight: 600;
  margin-left: 1px;
}

.reading--secondary .unit {
  color: currentColor;
  font-size: 0.92em;
  font-weight: 500;
}

.unit--deg {
  --a: var(--a-deg);
  --rho: var(--rho-deg);
}

.unit--mbs {
  --a: var(--a-mbs);
  --rho: var(--rho-mbs);
}

/* `%` 与中英文标签同为 0.82em 档却共用过一对系数：标签的墨迹主体与百分号不同源
   （百分号是两个小圈加斜杠），合起来拟合让两者的残差各涨到 1.1 设备px，各认一对即收回。 */
.unit--pct {
  --a: var(--a-pct);
  --rho: var(--rho-pct);
}

/* 连字符的墨迹坐在数学轴上，与数字不同源，故各认各的 ρ */
.is-missing .digits {
  --a: var(--a-dash);
  --rho: var(--rho-dash);
}

.is-missing,
.is-missing .unit {
  color: var(--bar-dim);
}
</style>
