/// <reference types="vite/client" />
import type { PerfApi } from '@shared/types';

declare module '*.vue' {
  import type { DefineComponent } from 'vue';
  const component: DefineComponent<{}, {}, any>;
  export default component;
}

declare global {
  interface Window {
    // preload 经 contextBridge 注入的唯一门面
    api: PerfApi;
  }
}

export {};
