<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import type { Settings, Theme } from '@shared/types';
import SettingsCard from './components/SettingsCard.vue';
import SwitchRow from './components/SwitchRow.vue';
import SelectRow from './components/SelectRow.vue';
import SliderRow from './components/SliderRow.vue';
import type { SelectOption } from './components/SelectRow.vue';

const FAST_OPTIONS: SelectOption[] = [
  { value: '1000', label: '1s' },
  { value: '2000', label: '2s' },
  { value: '5000', label: '5s' }
];
const SLOW_OPTIONS: SelectOption[] = [
  { value: '3000', label: '3s' },
  { value: '5000', label: '5s' }
];
const THEME_OPTIONS: SelectOption[] = [
  { value: 'system', label: '跟随系统' },
  { value: 'dark', label: '深色' },
  { value: 'light', label: '亮色' }
];

// 单一数据源仍是主进程：本地只持有最近一次已知快照，提交后由返回值/广播覆盖，
// 因此外部（另一处改配置、自启被系统纠正）的变更能自然回流到表单。
const settings = ref<Settings | null>(null);
const status = ref('');

// 保存反馈：只说「已保存」，两秒后自己退场。原先写成 `已保存 · 刷新 1s / 5s`，
// 是拿中点把元信息串成一行——用户刚动过的那个控件已经把「改了什么」说完了。
let statusTimer: ReturnType<typeof setTimeout> | null = null;

const s = computed(() => settings.value);
const opacityPct = computed(() => Math.round((s.value?.opacity ?? 0.72) * 100));

async function save(patch: Partial<Settings>): Promise<void> {
  settings.value = await window.api.setSettings(patch);
  status.value = '已保存';
  if (statusTimer !== null) clearTimeout(statusTimer);
  statusTimer = setTimeout(() => {
    statusTimer = null;
    status.value = '';
  }, 2000);
}

// metrics 子对象按键深合并：只提交改动的那一项
function setMetric(key: keyof Settings['metrics'], value: boolean): void {
  void save({ metrics: { ...settings.value!.metrics, [key]: value } });
}

let offSettings: (() => void) | null = null;

onMounted(() => {
  offSettings = window.api.onSettings((next) => {
    settings.value = next;
  });
  void window.api.getSettings().then((next) => (settings.value = next));
});

onBeforeUnmount(() => {
  offSettings?.();
  if (statusTimer !== null) clearTimeout(statusTimer);
});
</script>

<template>
  <div v-if="s">
    <header class="page-head">
      <h1>设置</h1>
    </header>

    <SettingsCard title="显示指标">
      <SwitchRow
        label="CPU 使用率"
        :model-value="s.metrics.cpu"
        @update:model-value="setMetric('cpu', $event)"
      />
      <SwitchRow
        label="内存"
        :model-value="s.metrics.mem"
        @update:model-value="setMetric('mem', $event)"
      />
      <SwitchRow
        label="GPU"
        hint="（仅 NVIDIA）"
        :model-value="s.metrics.gpu"
        @update:model-value="setMetric('gpu', $event)"
      />
      <SwitchRow
        label="网络"
        :model-value="s.metrics.net"
        @update:model-value="setMetric('net', $event)"
      />
      <SwitchRow
        label="时间"
        :model-value="s.metrics.time"
        @update:model-value="setMetric('time', $event)"
      />
    </SettingsCard>

    <SettingsCard title="刷新率">
      <SelectRow
        label="CPU / 内存 / 网络"
        :model-value="String(s.refreshFastMs)"
        :options="FAST_OPTIONS"
        @update:model-value="save({ refreshFastMs: Number($event) })"
      />
      <SelectRow
        label="GPU / 温度"
        :model-value="String(s.refreshSlowMs)"
        :options="SLOW_OPTIONS"
        @update:model-value="save({ refreshSlowMs: Number($event) })"
      />
    </SettingsCard>

    <SettingsCard title="外观">
      <SelectRow
        label="主题"
        :model-value="s.theme"
        :options="THEME_OPTIONS"
        @update:model-value="save({ theme: $event as Theme })"
      />
      <SliderRow
        label="背景不透明度"
        :display="`${opacityPct}%`"
        :model-value="opacityPct"
        :min="20"
        :max="100"
        :step="5"
        @update:model-value="save({ opacity: $event / 100 })"
      />
      <SliderRow
        label="字体大小"
        :display="String(s.fontSize)"
        :model-value="s.fontSize"
        :min="10"
        :max="18"
        :step="1"
        @update:model-value="save({ fontSize: $event })"
      />
    </SettingsCard>

    <SettingsCard title="行为">
      <SwitchRow
        label="开机自启"
        :model-value="s.autostart"
        @update:model-value="save({ autostart: $event })"
      />
      <SwitchRow
        label="其他应用全屏时自动隐藏"
        :model-value="s.autoHideOnFullscreen"
        @update:model-value="save({ autoHideOnFullscreen: $event })"
      />
    </SettingsCard>

    <p class="status">{{ status }}</p>
  </div>
</template>
