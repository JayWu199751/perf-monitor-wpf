<script setup lang="ts">
// 滑块在 input 事件上连续提交：拖动过程中就能看到小窗透明度/字号实时变化。
// 主进程侧的落盘是整文件重写，自用工具的写入频率可以接受。
defineProps<{
  label: string;
  modelValue: number;
  min: number;
  max: number;
  step: number;
  // 紧跟标签显示的当前值，如 "72%" / "12"
  display: string;
}>();

defineEmits<{ 'update:modelValue': [value: number] }>();
</script>

<template>
  <label class="row">
    <span>{{ label }} <b>{{ display }}</b></span>
    <input
      type="range"
      :min="min"
      :max="max"
      :step="step"
      :value="modelValue"
      @input="$emit('update:modelValue', Number(($event.target as HTMLInputElement).value))"
    />
  </label>
</template>
