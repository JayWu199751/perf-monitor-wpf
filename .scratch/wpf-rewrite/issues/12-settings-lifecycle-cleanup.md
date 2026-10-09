# 12: 设置窗生命周期与退出清理

**What to build:** 设置窗按需打开、关闭后不占用长期资源；应用退出或系统注销时干净关闭窗口、后台任务和原生资源。

**Blocked by:** 07: 指标设置、持久化与性能条布局；11: 共享菜单、托盘主题与透明显示。

**Status:** resolved

- [x] 设置窗按需创建；点关闭只隐藏，不退出主应用。
- [x] 隐藏闲置 5 分钟后释放设置窗实例；重新打开可取消待执行的释放并复用当前实例。
- [x] 设置窗按内容和当前屏幕工作区决定尺寸；短屏可滚动访问所有行。
- [x] 从退出命令、注销或关机结束时放行窗口关闭，停止后台工作并释放窗口、托盘及 native 资源。
- [x] 退出后无遗留 GPU 子进程、幽灵图标、计时器回调或无法退出的隐藏窗口。（代码路径已覆盖并有取消/终止语义测试；真实桌面观察见 Comments 人工复验项）

## Comments

### 2026-10-05（实现者）

**实现摘要**

- 设置窗生命周期状态机落在 Core（`StartupShellController`，`src/PerfMonitor.Core/StartupShell.cs`）：
  - `StartupShellState` 新增 `IsSettingsWindowRecyclePending`；打开设置窗前取消待执行回收（已释放则宿主按需重建），关闭只隐藏并按闲置间隔排程回收（默认 5 分钟，`settingsIdleRecycleDelay` 可注入），到期回调仅在「已创建且已隐藏且应用运行中」时经新增宿主端口 `IStartupShellHost.ReleaseSettingsWindow()` 释放实例；退出与 `Dispose()` 取消排程并释放定时器（新增 `ISettingsIdleRecycleTimer` 端口与默认单次 `SettingsIdleRecycleTimer`，`src/PerfMonitor.Core/Shell/SettingsIdleRecycleTimer.cs`）。Core 不依赖 WPF/Win32，定时器与间隔均可注入测试。
  - 懒创建与关闭即隐藏为既有行为（工票 07/13），本票补充并保持合同锁定。
- 尺寸规则：新增 `SettingsWindowSizingRules.ResolveMaxWindowHeight`（`src/PerfMonitor.Core/Shell/SettingsWindowSizingRules.cs`）：窗口 MaxHeight = 当前屏工作区可用高，非法输入回退 240 保底，不再照抄旧版固定 880。设置窗改 `SizeToContent="Height"`，`src/PerfMonitor.App/SettingsWindow.xaml.cs` 经 `MonitorFromWindow`/`GetMonitorInfoW`/`GetDpiForMonitor` 读取窗口所在显示器工作区（物理像素换算 DIP），在 `SourceInitialized`、`LocationChanged`（跨屏移动）与 `SystemParameters.WorkArea` 变化时重新约束；内容放得下无滚动条，短屏由内容区 `ScrollViewer` 滚动访问所有行。
- 退出与系统会话：`src/PerfMonitor.App/App.xaml.cs` 在 `SessionEnding`（注销/关机）时复用共享菜单退出链 `SelectMenuItem(Exit)`——停快慢采样 → flush 落位 → 停全屏 watcher → 状态清零 → 宿主 Shutdown（关上下文菜单、撤托盘图标先行、摘除设置窗关闭拦截后关闭窗口）→ `Application.Shutdown` → `OnExit` 释放控制器/宿主/单实例协调器（停任务栏守卫计时器、退订 SystemEvents、释放显示源与 VM）。`WpfStartupShellHost.ReleaseSettingsWindow` 在 UI 线程关闭并丢弃窗口引用，窗口自身的保存提示计时器与 SystemEvents 挂钩经其 `Closed` 处理退订。整条链幂等（`State.IsRunning`、`_shutdownRequested`、`_disposed` 三层守卫）。
- nvidia-smi 子进程：慢采样器 `Stop`/`Dispose` 取消在途 CTS，`WindowsSlowMetricsSource` 在取消/超时/异常路径 `Kill(entireProcessTree: true)` 并等待 reap（既有实现，本票核验无缺口）。

**测试证据**

- 命令：`dotnet test PerfMonitor.sln`；结果：PerfMonitor.Core.Tests 139 通过（原 122 + 新增 17），PerfMonitor.Windows.Tests 47 通过，合计 186 通过、0 失败、0 警告（基线 169 + 新增 17，无回归）。
- 新增合同（`tests/PerfMonitor.Core.Tests/StartupShellContractTests.cs`，注入 `ManualSettingsIdleRecycleTimer`）：关闭后排程回收且默认 5 分钟；间隔可注入；到期释放一次且不重复；闲置期内重开取消回收并复用实例；释放后重开重建并可再次回收；退出取消回收且到期不再释放；控制器 Dispose 释放定时器；到期时可见不释放（竞态守卫）；隐藏态重复关闭不重复排程。
- 新增 `SettingsIdleRecycleTimerTests`（5 项，真实定时器短间隔）：到期触发一次、取消不触发、重复排程替换、释放后失效、负延迟拒绝。
- 新增 `SettingsWindowSizingRulesTests`（3 项）：MaxHeight 跟随工作区而非固定 880、过小工作区保底、非法输入回退。
- 集成：`git merge codex/wpf-rewrite-integration` 结果 Already up to date（分支基于当前集成 tip 86fe71b），全量构建 + 测试通过后提交。

**未自动验证项（人工复验步骤）**

1. 真实桌面 5 分钟闲置回收观察：启动应用 → 打开设置 → 点关闭 → 等 5 分钟 → 再次打开设置，确认窗口正常重建、表单值为当前保存值；任务管理器观察私有内存回落。可用 FakeTimeProvider 之外的实机路径验证默认间隔未被破坏。
2. 注销/关机路径：启动应用（设置窗可保持打开）→ 注销或重启系统 → 重新登录后确认无进程残留、无幽灵托盘图标。自动化以 SessionEnding 合同与退出链幂等测试替代，未做真实会话终止。
3. 无遗留 nvidia-smi 子进程：在有 NVIDIA 显卡的机器上启动应用 → 等待若干轮慢采样 → 从共享菜单退出 → 任务管理器/`Get-Process nvidia-smi` 确认无残留进程。
4. 无幽灵托盘图标与隐藏窗口：退出后确认托盘区图标立即消失、无不可见窗口卡住 Explorer（Alt+Tab / 任务管理器应用列表为空）。
5. 多显示器 + 混合 DPI 下的尺寸约束：设置窗拖到低分辨率副屏后确认 MaxHeight 按该屏工作区收缩并出现滚动条，拖回主屏恢复。


### 2026-10-10：用户人工验收通过

- 用户明确确认‘除了内存我已全部验证通过’。本事项非内存验收已通过，状态更新为 `resolved`。
- 证据为用户整体人工确认，详见 [验收记录](../acceptance-2026-10-10.md)；未补写逐项操作过程或测量数据。正文及旧 Comments 中‘待人工验收/未验证’等表述保留为历史情况，以本次确认更新当前结论。
