<script setup lang="ts">
export interface SelectOption {
  value: string;
  label: string;
}

defineProps<{
  label: string;
  modelValue: string;
  options: SelectOption[];
}>();

defineEmits<{ 'update:modelValue': [value: string] }>();
</script>

<template>
  <label class="row">
    <span>{{ label }}</span>
    <select
      :value="modelValue"
      @change="$emit('update:modelValue', ($event.target as HTMLSelectElement).value)"
    >
      <option v-for="opt in options" :key="opt.value" :value="opt.value">{{ opt.label }}</option>
    </select>
  </label>
</template>
