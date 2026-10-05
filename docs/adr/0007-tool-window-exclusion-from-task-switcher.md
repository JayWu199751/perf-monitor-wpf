# 应用窗口以工具窗口样式隐匿任务切换器

> 状态：Accepted

## 决策

- 性能条与设置窗在 `ShowInTaskbar=false` 之外，统一于窗口 Loaded 后补写 `WS_EX_TOOLWINDOW`（`NativeWindowStyles.ExcludeFromTaskSwitcher`，经 `NativeMethods` 的 `GetWindowLongW/SetWindowLongW`）。窗口由此从任务栏、Alt+Tab 与任务视图整体隐匿；应用仅以托盘图标与桌面悬浮卡片露出。
- 设置窗开启期间同样不占任务栏与任务切换器；失焦后经托盘「设置」找回（现有逻辑复用实例并前置激活）。
- 注入时机选在 Loaded：WPF 在 Show 流程后段才完成所有权与样式设置，SourceInitialized 时注入会被覆盖。
- 不采用 `Visibility=Hidden` 方案（锁屏后与 BitmapCache 组合存在渲染失效的已知问题）；不清理所有权机制——工具窗口样式与所有权叠加无害，注入幂等（已置位即跳过），避免重复样式写入。

## 依据

运行时实测（2026-10-05，`.scratch/win_style_probe.py`）：修复前性能条 exstyle=`0x00080008`（无 `WS_EX_TOOLWINDOW`），并被 WPF 自建的隐藏窗口持有（owner 类 `HwndWrapper[PerfMonitor.App]`，标题 "Hidden Window"）。新版 WPF 对 `ShowInTaskbar=false` 的实现是挂靠隐藏所有者窗口而非工具窗口样式：所有权只压掉任务栏按钮，Windows 11 的 Alt+Tab 与任务视图仍列出被持有的可见窗口，常驻悬浮条因此泄漏进任务切换器（用户截图证实，2026-10-05）。工具窗口样式是 shell 对任务栏、Alt+Tab、任务视图三处一致排除的稳定机制，与所有权机制正交。修复后同法实测 exstyle=`0x00080088`（含 `WS_EX_TOOLWINDOW`）。窗口样式属壳层/视觉行为，无自动化合同，列入验收矩阵 A47 复验。
