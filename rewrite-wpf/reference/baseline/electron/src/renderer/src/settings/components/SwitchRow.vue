<script setup lang="ts">
defineProps<{
  label: string;
  // 括号里的补充说明，如 GPU 的"仅 NVIDIA"
  hint?: string;
  modelValue: boolean;
}>();

defineEmits<{ 'update:modelValue': [value: boolean] }>();
</script>

<template>
  <label class="row">
    <span>{{ label }}<span v-if="hint" class="hint">{{ hint }}</span></span>
    <input
      type="checkbox"
      class="switch"
      :checked="modelValue"
      @change="$emit('update:modelValue', ($event.target as HTMLInputElement).checked)"
    />
  </label>
</template>
