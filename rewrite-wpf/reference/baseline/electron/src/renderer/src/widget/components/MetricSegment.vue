<script setup lang="ts">
// 小窗里的一段：可选标签 + 读数容器。title 提供悬停说明。
defineProps<{ label?: string; title?: string }>();
</script>

<template>
  <div class="seg" :class="{ 'seg--time': !label }" :title="title">
    <span v-if="label" class="label optical">{{ label }}</span>
    <span class="val"><slot /></span>
  </div>
</template>

<style scoped>
.seg {
  display: inline;
  line-height: var(--lh-widget);
}

.label {
  --a: var(--a-label);
  --rho: var(--rho-label);
  /* 标签到首读数的这段空气由这里独占。此前它一半藏在对齐槽的左侧（槽里放不下的位数
     才轮到右对齐去挤），读数一到三位数就把可见间距吃到 3px——见 ADR-0006 修正七。
     随 --fs 缩放（0.58 ≈ fs12 时的 7px）：字号是唯一的用户量，固定 px 会让 fs18 相对变紧。 */
  margin-right: calc(var(--fs, 12px) * 0.58);
  color: var(--bar-label);
  font-size: clamp(9px, 0.82em, 12px);
  font-weight: 600;
  line-height: var(--lh-widget);
}

.val {
  color: var(--bar-text);
  line-height: var(--lh-widget);
}

.seg--time .val {
  color: var(--bar-secondary);
  font-weight: 600;
}
</style>
