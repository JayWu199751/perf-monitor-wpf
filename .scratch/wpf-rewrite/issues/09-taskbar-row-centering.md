# 09: 任务栏行内布局与水平居中

**What to build:** 用户可将同一张性能条拖入顶部或底部任务栏行，并选择在松手后让它回到该行水平中心。

**Blocked by:** 08: 贴边、多显示器与位置恢复。

**Status:** ready-for-human

- [x] 顶部或底部水平任务栏行由显示器 bounds 与 work area 的差值识别；左右任务栏不作为此功能的落点。（自动化已证实）
- [x] 卡片中心进入任务栏行后，卡片垂直居中到该行；行内横向位置默认跟随用户拖动。（自动化已证实）
- [x] 共享菜单的“任务栏内水平居中”开关持久化；开启时拖动跟手、松手回到整行水平中心，关闭时保持松手位置。（开启/关闭结算语义与持久化已自动化证实；真实菜单点击待人工复验）
- [x] 横向参照使用整个任务栏行宽度，不避让开始按钮、托盘或应用图标；覆盖区域会拦截底下任务栏点击。（自动化证实居中参照为整行矩形；拦截点击为覆盖语义自然结果，待人工确认）
- [ ] 在真实桌面验证顶/底任务栏、点击任务栏切换应用后的可见性恢复和 Explorer 重启情形；不得仅凭 Topmost 属性宣称通过。（留人工复验）

## Comments

### 2026-10-05（impl-09-taskbar）

**实现摘要**

- `PerfMonitor.Core/Shell/TaskbarRowRules.cs`（新增，纯几何）：`DeriveRows` 按 `bounds` 与 `work area` 差分派生顶/底水平行（顶行在前，横向覆盖整个显示器宽度）；左右侧任务栏（工作区左右收窄）不产生行；无差分（无任务栏）返回空。`SettleRowPlacement`：卡片中心位于行内（左闭右开，`[row.Y, row.Bottom)`）时垂直居中到行（`y = row.Y + (row.Height - frame.Height) / 2`）并把掩码叠加对应 `Top`/`Bottom`（保留既有左右贴边）；`centerInRow` 开启时再弹回整行水平中心（`x = row.CenterX - frame.Width / 2`）；行外原样返回普通贴边结果（回落到 08 的 `PlacementRules.SettleSnap`）。同文件新增 `ITaskbarVisibilityGuardPort`（`EnsureAboveTaskbar` / `RefreshTaskbarHandles` / `OnGuardStopped`），Core 规则不依赖 WPF/Win32。
- `StartupShellController`（`StartupShell.cs`）：`SettlePlacement` 在 08 的贴边结算之后衔接 `SettleRowPlacement`，并维护 `_isInTaskbarRow`；「任务栏内水平居中」菜单开启立即 `SettlePlacement()` 一次（弹回行中心），关闭原地不动（不结算）；`RestorePlacementIfPortChanged` 恢复位置后重新结算一次以衔接行内落点与守卫；原生拖动中不运行守卫 tick（沿用「原生真实框架读取后结算、重入门控防递归」机制）。行内落位改变位置时掩码随 `WidgetPlacement.Docked` 一并持久化（防抖/flush 沿用 08）。
- 守卫节奏在 Core：构造参数 `taskbarGuardFastMilliseconds`（默认 30）/ `taskbarGuardSlowMilliseconds`（默认 300）驱动两个 `Timer`；快 tick 调 `EnsureAboveTaskbar`（z 序遮挡检查），慢 tick 调 `RefreshTaskbarHandles`（句柄刷新）。tick 回调前置条件：运行中、性能条可见、非原生拖动；隐藏时 `StopTaskbarGuard` 停表并回调 `OnGuardStopped`（adapter 把卡片放回普通 z 序层）；悬浮态（`_isInTaskbarRow` 为 false）守卫完全不启动。`IStartupShellHost` 增量新增可替换端口 `TaskbarVisibilityGuard`（默认 `null`）。
- `PerfMonitor.Windows/Shell/WindowsTaskbarVisibilityGuard.cs`（新增）：遮挡判据为卡片中心点 `WindowFromPoint` 的最顶层根窗口（`GetAncestor(GA_ROOT)`）类名归属 `Shell_TrayWnd`/`Shell_SecondaryTrayWnd`——根窗口归属判据，不按截图、不按 `Topmost` 属性；被任务栏遮挡时 `SetWindowPos(HWND_TOPMOST)` 恢复可见性；`RefreshTaskbarHandles` 用 `EnumWindows` 枚举任务栏根窗口句柄（explorer 重启后按慢周期自动恢复）；`OnGuardStopped` 把卡片放回 `HWND_NOTOPMOST`。空/无效句柄安全跳过。
- `WpfStartupShellHost`：懒创建 `WindowsTaskbarVisibilityGuard`（卡片句柄取自 `PerformanceBarWindow.RootWindowHandle`，窗口未加载返回 0），经 `DispatchedTaskbarGuard` 编组到 UI 线程执行（`SetWindowPos` 作用于 UI 线程所属窗口），pending 标志防 UI 繁忙时回调积压。
- 菜单与设置：共享菜单沿用基础已有的「任务栏内水平居中」勾选项（真值读中央 `Settings`）；`SettingsPatch` 既有 `CenterInTaskbarRow` 字段仅服务菜单路径，设置表单不提供该入口（`SettingsWindow` 无对应控件）；`centerInTaskbarRow` 随 `PerformanceSettings` 走 `WindowsSettingsStore`（schemaVersion 1 原子写入）持久化。

**测试证据**

- 命令：`dotnet build PerfMonitor.sln`（0 警告 0 错误）、`dotnet test`（本分支 + 合并集成分支后均全过）。
- 新增测试：
  - `TaskbarRowRulesTests`（Core，17 条）：顶/底行差分派生、左右任务栏无行、顶底同时存在两行、全屏工作区无行、行内垂直居中（顶/底）、centerInRow 弹回整行中心、保留左右贴边叠加行贴边、行外回落、中心左闭右开边界、整行宽度居中不避让。
  - `PerformanceBarPlacementContractTests` 增补（Core，8 条）：拖入底行垂直居中横向跟手（掩码持久化 Bottom）、拖出回落普通贴边（掩码解除 Bottom）、开启立即结算弹回行中心、关闭原地不动、行外开启不生效、行内运行守卫且慢周期刷新句柄、悬浮态不运行守卫、隐藏后守卫停止且 `OnGuardStopped` 恰好一次。全部走 `StartupShellController` 合同 + 假端口/假显示器源/假守卫端口（守卫节奏注入 10/30ms）。
  - `WindowsTaskbarVisibilityGuardTests`（Windows.Tests，4 条集成）：空句柄安全、真实桌面枚举 ≥1 个任务栏根窗口、重复枚举一致（explorer 重启恢复路径）、无效句柄跳过。
  - `WindowsSettingsStoreTests` 增补 `CenterInTaskbarRow = true` 到 roundtrip 用例。
- 合并 `codex/wpf-rewrite-integration`（294dbf6，含事项 10）冲突按多端口共存解决（`TaskbarVisibilityGuard` 与 `FullscreenWatcher` 并存）后全量：Core 120 通过、Windows 32 通过（基线 98 项无回归）。

**提交**

- `05769bc` 实现 F08 任务栏行内落点、行内居中与 z-order 可见性守卫
- `1bb6750` 合入集成分支：守卫端口与全屏观察端口共存

**未能自动验证（需人工复验）**

自动化无法覆盖真实桌面交互，请按以下步骤人工复验：

1. 顶/底任务栏拖入：真实桌面把小窗拖到底部任务栏上松手，应垂直居中到任务栏行、横向停在松手位置；再拖到顶部任务栏（任务栏置于顶部时）同样垂直居中。左右任务栏布局下拖入侧边不应触发行内落点（保持普通贴边）。
2. 行内水平居中开关：右键菜单勾选「任务栏内水平居中」，小窗应立即弹回整行水平中心（覆盖开始按钮/托盘/图标区域，不避让——覆盖处会拦截任务栏点击，属预期代价）；行内拖动跟手，松手回中；取消勾选后拖动松手停在原地；行外开启开关无位置变化。重启应用后勾选状态保持。
3. 切应用后可见性恢复：小窗驻留任务栏行内时，前台启动其他应用（任务栏 z 序变化），观察小窗不被任务栏遮挡、仍可点击拖动；判据以实际可见性为准（非 Topmost 属性）。
4. Explorer 重启：小窗驻留行内时在任务管理器重启 Windows 资源管理器，任务栏重建后守卫应自动恢复遮挡检查（句柄按 300ms 慢周期刷新）；同时托盘图标重建属事项 11 范围，此处只验证遮挡恢复。
5. 隐藏与悬浮回落：托盘左键隐藏小窗后守卫停止（无原生遮挡检查活动）；把小窗拖离任务栏行后不应再常驻 topmost 层（守卫停止时已放回普通 z 序层）。
