<script setup lang="ts">
import Reading from './Reading.vue';
import { NET_DIGITS } from '../../lib/netSlot';

// 上下行两读数。方向箭头作为 Reading 的前置插槽内容，紧挨数字（定宽槽已在 2026-09-30 撤掉，
// 见 ADR-0006 修正七）。箭头与数字在同一条共享基线上各占一个 inline 盒，
// 因此它自己的 ρ 生效，不会再叠一次父级抬量（老结构里 .direction 的 translateY 叠在
// .reading 之上，实测让箭头比数字低整 1 设备px）。
defineProps<{ down: number | null; up: number | null }>();
</script>

<template>
  <Reading
    class="net-reading"
    :value="down"
    unit="MB/s"
    :digits="NET_DIGITS"
    ><span class="direction">↓</span></Reading
  >
  <Reading
    class="net-reading"
    :value="up"
    unit="MB/s"
    :digits="NET_DIGITS"
    ><span class="direction">↑</span></Reading
  >
</template>

<style scoped>
/* ρ 与归位都由 Reading.vue 的 .slot 认领，这里只负责配色 */
.net-reading :deep(.direction) {
  color: var(--bar-accent);
}

/* 缺失态下箭头不再强调，与变暗的数字同色 */
.net-reading :deep(.is-missing .direction) {
  color: currentColor;
}
</style>
