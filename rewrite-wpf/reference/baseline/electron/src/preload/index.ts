import { contextBridge, ipcRenderer } from 'electron';
import type { MetricsSnapshot, PerfApi, Settings } from '../shared/types';

// 渲染层的唯一入口门面：只暴露 5 个方法，不泄漏 ipcRenderer / Node 能力。
// 事件订阅返回解绑函数，Vue 组件在 onUnmounted 里调用即可。
const api: PerfApi = {
  onMetrics(cb) {
    const listener = (_e: Electron.IpcRendererEvent, s: MetricsSnapshot): void => cb(s);
    ipcRenderer.on('metrics', listener);
    return () => ipcRenderer.removeListener('metrics', listener);
  },
  onSettings(cb) {
    const listener = (_e: Electron.IpcRendererEvent, s: Settings): void => cb(s);
    ipcRenderer.on('settings', listener);
    return () => ipcRenderer.removeListener('settings', listener);
  },
  getSettings: () => ipcRenderer.invoke('settings:get') as Promise<Settings>,
  setSettings: (patch) => ipcRenderer.invoke('settings:set', patch) as Promise<Settings>,
  resizeWidget: (width, height) => ipcRenderer.send('widget:resize', { width, height })
};

contextBridge.exposeInMainWorld('api', api);
