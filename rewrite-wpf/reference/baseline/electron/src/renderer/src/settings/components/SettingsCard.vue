<script setup lang="ts">
// 分组卡片：一个标题 + 若干 .row。行间发丝分隔线由 CSS 相邻选择器负责。
defineProps<{ title: string }>();
</script>

<template>
  <section class="card">
    <h2>{{ title }}</h2>
    <slot />
  </section>
</template>
