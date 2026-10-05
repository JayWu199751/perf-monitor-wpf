# 11: 共享菜单、托盘主题与透明显示

**What to build:** 用户从托盘或性能条右键均可使用同一菜单，并能切换任务栏居中和透明显示；菜单真值、托盘图标和性能条外观保持同步。

**Blocked by:** 07: 指标设置、持久化与性能条布局；09: 任务栏行内布局与水平居中；10: 全屏自动隐藏与采样暂停。

**Status:** ready-for-human

- [x] 托盘和性能条右键共享同一组命令与勾选状态，菜单顺序为显示/隐藏、打开设置、任务栏内水平居中、透明显示、分隔线、退出。
- [x] 右键不先触发拖动，也不因重复的原生消息弹出两个菜单；菜单命令与中央设置真值一致。
- [x] 透明显示只移除背景、描边/阴影、分隔线和模糊；文字、箭头、时钟及命中区域仍可见、可拖动和可右键。
- [x] 保留黑色三柱应用图标和与有效主题对应的明/暗托盘图标；主题或 DPI 改变时选用匹配的图标资源。
- [x] Explorer 重启后托盘图标重新注册；退出时释放图标与菜单资源，不留幽灵图标。

## Comments

### 2026-10-05（实现者）

**实现摘要**

- 共享菜单命令模型在 Core 层（`StartupShellController.ShowContextMenu` 统一构建 `ShellMenuItem` 列表，托盘与性能条两处 origin 消费同一份数据；命令经 `SelectMenuItem` 写中央 `Settings` 真值并持久化）。`transparentDisplay` 默认 false、仅共享菜单入口、设置窗不露出（沿 `centerInTaskbarRow` 惯例）；透明往返保留 `Opacity` 原值。
- 透明显示视觉在 `PerformanceBarViewModel.UpdateThemeBrushes`：仅把卡片背景、描边、分隔线的 alpha 置 0 并把 `BorderThickness` 归零，文字/箭头/时钟刷子不变；整窗命中区域不变（背景保持 Alpha=1 分层窗口），可拖动、可右键。
- 新增 `PerfMonitor.Windows/Shell/TrayIconCatalog`：有效浅色主题选黑图 `tray-light-*`、深色选白图 `tray-dark-*`；物理 px 按 `16 × DPI` 就近匹配 16/20/24/28/32（等距时稳定取较小档）。资产复用旧版冻结源码 `rewrite-wpf/reference/baseline/resources/tray-*.png`。
- 新增 `PerfMonitor.App/Shell/TrayIconController` 托管 WinForms NotifyIcon：tooltip「性能小窗」、左键单击切换显隐、PNG→HICON 按需加载；Dispose 先 `Visible=false` 再释放 NotifyIcon 与 HICON（`DestroyIcon`），并入 host 的 `Shutdown`/`Dispose` 退出路径（与工票 10 的 StopFullscreenWatcher、08 的 placement flush 共存）。
- 主题/DPI 刷新：host 在 `ApplySettings` 检测 Theme 变化时刷新托盘图标；`SystemEvents.UserPreferenceChanged`（system 主题跟随 OS）与 `DisplaySettingsChanged`（DPI/显示变化）均编组回 UI 线程刷新。
- 新增 `ContextMenuPresentationGuard`：菜单已打开或距上次打开 200ms 内（重复原生消息 WM_RBUTTONUP + WM_CONTEXTMENU）不重复弹出；右键不触发拖动由性能条左键/右键分离的事件处理保证。
- explorer 重启重注册依赖 WinForms NotifyIcon 内建的 TaskbarCreated 监听（收到广播后自动重新添加图标），无需自建消息窗口。

**测试证据**

- 基线 152 项测试全过、0 警告；本次新增后全仓 `dotnet test` 169 项全过、0 警告（PerfMonitor.Core.Tests 122 + PerfMonitor.Windows.Tests 47）。
- Core 合同测试新增/已有覆盖：托盘与性能条右键菜单项及顺序一致、透明显示切换即时生效并持久化且两处菜单勾选一致、透明往返保留 Opacity 0.45、托盘左键切换显隐、性能条左键原生移动与右键菜单语义分离、退出清理整壳。
- Windows.Tests 新增：`TrayIconCatalogTests`（明暗前缀、10 组 DPI 就近档位、主题翻转保尺寸）、`ContextMenuPresentationGuardTests`（菜单已开抑制、窗口内重复消息抑制、窗口后放行）。

**未自动验证项（人工复验步骤）**

1. 真实托盘交互：启动 Release 构建，确认托盘 tooltip 为「性能小窗」；左键单击（非双击/悬停）切换小窗显隐；托盘右键弹出的菜单与性能条右键一致；在 200ms 内快速重复右键确认只弹一次。
2. explorer 重启重注册：任务管理器重启 Windows 资源管理器，确认托盘图标自动恢复；退出应用后托盘区无残留幽灵图标（悬停检查空位）。
3. DPI 图标切换：主屏缩放 100%/125%/150%/175%/200% 下观察托盘图标清晰度（就近档位而非拉伸模糊）；切换缩放后图标即时更新。
4. 主题切换：Windows 设置切换深色/浅色，确认 theme=system 时托盘图标黑/白即时切换；设置窗改 theme=dark/light 后托盘同步。
5. 透明显示真机效果：勾选透明显示后卡片背景/描边/分隔线消失、文字箭头时钟保留；仍可拖动与右键；退出透明显示后背景按原不透明度恢复。
