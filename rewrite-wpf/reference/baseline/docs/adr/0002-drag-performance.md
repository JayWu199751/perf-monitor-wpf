# 拖动期间优先小窗响应

> 状态：Accepted。拖动期缓冲见 `electron/src/main/metrics.ts`（`setMoving` + 单槽 `pending`）。
>
> 注意本文那个 216ms 实测值来自当时的 `systeminformation.networkStats()`（每轮起一个 PowerShell 子进程），网络源已换成进程内 `iphlpapi`（ADR-0004），该数值作废。本机复测新链路 p50 3.7ms / max 4.4ms，比原来低两个数量级；但本文的决策本身（拖动期暂停采样、只投递最新快照）与数据源快慢无关，照旧遵守。


Windows 原生拖动期间，性能小窗优先保持移动响应：收到 `WM_ENTERSIZEMOVE` 后暂停指标采样，并缓冲指标投递；收到 `WM_EXITSIZEMOVE` 后立即补采样且只渲染最新快照。本机测量显示 `systeminformation.networkStats()` 可使主进程事件循环停顿约 216ms，因此允许拖动时数值短暂静止，换取连续移动手感；位置记忆也改为 500ms 防抖写盘，并在小窗关闭时 flush 最终位置。

## Considered Options

- 保持拖动时实时采样和渲染：数值连续，但网络/温度读取和 DOM 重排会与原生移动竞争主线程，已实测产生卡顿。放弃。
- 将全部指标读取迁移到工作线程：可保留拖动时的实时数值，但会引入跨线程采样状态、PowerShell 生命周期和错误恢复复杂度；当前单机工具先采用暂停/补采样策略。暂不采用。
