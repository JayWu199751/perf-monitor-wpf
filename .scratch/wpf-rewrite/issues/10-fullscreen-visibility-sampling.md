# 10: 全屏自动隐藏与采样暂停

**What to build:** 全屏应用出现时性能条自动隐藏，退出全屏后按用户的手动选择恢复；隐藏期间暂停指标采样以减少资源占用。

**Blocked by:** 02: 原生性能条窗口交互与 DPI；04: CPU 与内存指标；05: 物理网卡网速；06: GPU 与 ACPI 温度；07: 指标设置、持久化与性能条布局。

**Status:** resolved

- [x] 设置提供默认开启的全屏自动隐藏选项；识别无边框 F11 全屏，最大化普通窗口不算全屏，并排除应用自身、桌面和任务栏。
- [x] 仅在进入/退出全屏的状态边沿处理显隐；手动隐藏优先，用户在同一全屏周期内手动显示后保持显示。
- [x] 关闭自动隐藏时，仅恢复当前因自动原因隐藏的性能条；手动隐藏状态仍保持隐藏。
- [x] 性能条隐藏时暂停快慢指标采样；全屏观察继续运行，显示时立即补采样。暂停后的首次网络采样建立新基线。
- [x] 时钟按自身节奏更新；退出时停止全屏观察和采样任务，不遗留后台投递。

## Comments

### 2026-10-05 实现摘要（agent）

**提交**：`3594b2f 实现全屏自动隐藏与隐藏期采样暂停`（基于 `e818193`，已合入集成 tip `4b91396`，合并提交 `3760e55`）。

**Core（`src/PerfMonitor.Core/StartupShell.cs`）**
- 新增 `IFullscreenWatcher` 能力端口（`Start(Action<bool>)`/`Stop()`/`IDisposable`）与 `IStartupShellHost.FullscreenWatcher` 注入点；Core 不依赖 WPF/Win32。
- `StartupShellController` 实现规格 F09 状态转移表：进入全屏边沿隐藏并标记自动隐藏（`activate:false` 不抢焦点）；退出全屏恢复且不抢焦点；手动隐藏标记 `manualHidden` 优先，全屏进出保持隐藏；自动隐藏期间手动显示（托盘/菜单切换、重复启动唤起 `OnRepeatedLaunchRequested`）清两种隐藏标记并抑制同一全屏周期的再次隐藏；手动显示后新一轮进入全屏恢复正常规则；重复的边沿通知幂等（仅状态翻转处理）。
- `UpdateSettings` 处理 `autoHideOnFullscreen` 开关：关闭时停止 watcher 并仅恢复因自动原因隐藏的状态，手动隐藏保持；开启时启动 watcher（watcher 首轮即报告当前状态，当前全屏则按状态机隐藏）。
- 自动隐藏/恢复走与手动显隐相同的 `SetPerformanceBarVisibilityCore`，复用既有 `CanSampleMetrics` 门控：隐藏暂停快慢采样（代际隔离），显示立即补采样。
- 采样器验证结论：`PerformanceMetricsSampler.SampleGenerationAsync` 每代新建 `NetworkThroughputTracker`，暂停后重启的首轮读数无基线自然返回 0（新基线），下一轮才算速率；Core 测试已断言该行为，无需改动。
- 时钟为 `PerformanceBarViewModel` 内独立 `DispatcherTimer`，与采样无关，未受影响。
- 生命周期：`Start` 时按设置启动 watcher；菜单退出与 `Dispose` 停止 watcher 与采样任务并重置状态标记。

**Windows adapter（`src/PerfMonitor.Windows/Fullscreen/WindowsFullscreenWatcher.cs`）**
- 默认每秒轮询 `GetForegroundWindow`；注入点：前台窗口源、窗口探测源、显示器边界源、本进程 PID、回调 `SynchronizationContext`（WPF 宿主在 UI 线程构造，边沿通知送回 UI 线程）。
- 全屏判定 `FullscreenWindowRules`：排除类名 Progman/WorkerW/Shell_TrayWnd/Shell_SecondaryTrayWnd 与本进程窗口；须可见且未 DWM cloak；带 `WS_CAPTION`（含普通最大化）不算全屏；无边框窗口即使带 `WS_MAXIMIZE`（Chrome F11）覆盖对应显示器统一物理矩形（`GetWindowRect` vs `MONITORINFO.rcMonitor`，进程 PerMonitorV2，不做额外缩放），边界容差 2 物理px。
- 仅在进入/退出全屏状态翻转时通知（首轮初始状态必报一次，Core 端幂等消费）。
- 设置窗 `autoHideOnFullscreen`（默认 true）UI、补丁与 JSON 持久化在集成基线已具备（`SettingsWindow.xaml` 行为组、`PerformanceSettings`/`SettingsPatch`/`WindowsSettingsStore` 整体序列化），本次仅消费该设置。

**测试证据（自动化，`dotnet test PerfMonitor.sln` 0 警告 0 错误，94 通过）**
- `FullscreenVisibilityContractTests`（Core，16 个用例）：F09 状态转移表全部行——可见→全屏隐藏不抢焦点；退出恢复不抢焦点；手动隐藏跨越全屏进出保持；同周期手动显示保持+退出不重复显示+新一轮恢复正常；重复启动唤起走手动显示；关闭自动隐藏仅恢复自动隐藏的、手动隐藏保持；重新开启恢复观察；重复边沿幂等；watcher 生命周期（启动/退出/Dispose/关闭设置不启动）；自动隐藏暂停快慢采样且恢复立即补采样；暂停后首轮网络 0 基线、下一轮 3 MB/s。
- `WindowsFullscreenWatcherTests`（Windows，8 个用例）：系统类排除、无边框覆盖（含 WS_MAXIMIZE）判全屏、带标题栏最大化不算、2px 容差边界值、不可见/cloaked 排除、本进程排除、仅边沿通知、混合 DPI 统一物理坐标。

**未自动验证项（需人工复验）**
1. 真实桌面无边框 F11 全屏：打开 Chrome/Edge 按 F11，性能条应在约 1 秒内隐藏；退出 F11 应恢复且不抢焦点。再把性能条手动隐藏后进出 F11，应保持隐藏。
2. 多显示器混合 DPI：在两个不同缩放比（如 100% 与 150%）显示器上分别 F11 全屏，均应自动隐藏；Windows 徽标键切回桌面后恢复。
3. 真实应用切换：全屏游戏/视频播放器与普通窗口间 Alt+Tab，显隐随全屏边沿切换；同全屏周期内从托盘手动显示后应持续可见。


### 2026-10-10：用户人工验收通过

- 用户明确确认‘除了内存我已全部验证通过’。本事项非内存验收已通过，状态更新为 `resolved`。
- 证据为用户整体人工确认，详见 [验收记录](../acceptance-2026-10-10.md)；未补写逐项操作过程或测量数据。正文及旧 Comments 中‘待人工验收/未验证’等表述保留为历史情况，以本次确认更新当前结论。
