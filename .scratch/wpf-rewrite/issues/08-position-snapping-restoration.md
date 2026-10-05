# 08: 贴边、多显示器与位置恢复

**What to build:** 用户可拖动性能条贴靠屏幕工作区边缘，并在重启、DPI 变化或显示器插拔后找回它。

**Blocked by:** 07: 指标设置、持久化与性能条布局。

**Status:** ready-for-human

- [x] 拖动支持工作区四边和四角的 8 DIP 贴边阈值；卡片可视边缘贴边并保留约 1 DIP 安全边框。
- [x] 透明宿主的额外留白不会妨碍卡片贴边；向屏幕内拖过阈值后解除对应贴边。
- [x] 位置与贴边状态由窗口行为统一保存，约 500ms 防抖；关闭时写入最后位置，期间的其他设置更改不会覆盖位置。
- [x] 只保存一份最后位置；按卡片中心定位目标显示器，支持负坐标及不同 DPI，显示器消失时将卡片夹回可见工作区。
- [ ] 工作区、分辨率或 DPI 改变后重新校正落点；真实桌面验证四边四角、多显示器、插拔和重启恢复。

## Comments

### 2026-10-05（impl-08-snapping）

**实现摘要**

- `PerfMonitor.Core/Shell/PlacementRules.cs`：纯几何规则，坐标统一为物理像素（统一处理负坐标与混合 DPI）。四边+四角 8 DIP 阈值（按目标显示器 `DpiScale` 缩放）、1 DIP 安全边框；已贴边被推出工作区外侧保持吸附，仅向屏幕内离开阈值解除（sticky 语义）；`PlacementInsets` 支持透明宿主留白——按卡片可视边缘结算并把透明部分推出工作区（当前 WPF 宿主无可视边距， insets 为 0，规则已内建）；按卡片中心解析目标显示器（含负坐标），中心不在任何显示器内时取最近显示器并夹回可见工作区；恢复时先夹回再重放贴边掩码。
- `PerfMonitor.Core/Shell/PlacementPersistence.cs`：500ms 防抖持久化（定时器可注入更短周期供测试），支持退出/销毁 flush、设置合并后取消挂起。
- `StartupShellController`（`StartupShell.cs`）为唯一位置写入者：只维护一份 `_lastPlacement`；启动/窗口重建（端口实例变化）时把待恢复位置写入窗口框架，创建期临时位置从不参与保存；原生拖动结束（`BeginPerformanceBarNativeMove` 返回）读取原生真实框架 → 按中心解析显示器 → 结算贴边 → 写回，重入门控 `_isSettlingPlacement` 防摆窗递归；防抖期间 `UpdateSettings` 合并最新位置到同一次保存；退出菜单与 `Dispose` 均 flush；显示器变化（`IDisplayEnvironmentSource.DisplaysChanged`）触发重新落位。设置表单无法写位置（`SettingsPatch` 无 Widget 字段）。
- `IStartupShellHost` 增量新增两个可替换端口（默认 `null`，不破坏并行工票）：`DisplayEnvironmentSource`、`PerformanceBarPlacement`。控制器新增可选参数 `placementDebounceMilliseconds`（默认 500）。
- `PerfMonitor.Windows/Displays/WindowsDisplayEnvironmentSource.cs`：`EnumDisplayMonitors` + `GetMonitorInfo` + `GetDpiForMonitor`（shcore 缺失时回退 1.0）；不可见顶层窗口接收 `WM_DISPLAYCHANGE`/`WM_SETTINGCHANGE` 广播触发重新落位。资源释放：销毁窗口、释放后枚举返回空、Dispose 幂等。
- `PerfMonitor.Windows/Displays/NativeWindowFrame.cs`：`GetWindowRect`/`SetWindowPos` 物理像素读写，供 WPF 窗口实现落位端口，避开混合 DPI 下的 DIP 换算歧义。
- `PerformanceBarWindow` 实现 `IPerformanceBarPlacementPort`；`WpfStartupShellHost` 接通两端口并在 Dispose 释放显示器源。未触碰工票 07 的峰值宽规则（宽度只增不减、相同尺寸不重摆）。

**测试证据**

- 命令：`dotnet build PerfMonitor.sln`（0 警告 0 错误）、`dotnet test PerfMonitor.sln`。
- 新增测试：
  - `PlacementRulesTests`（Core，14 条）：阈值/安全边框/四角组合/DPI 缩放/sticky 解除/透明 insets/中心解析（负坐标）/最近显示器/夹回/恢复重放掩码/掩码规范位序。
  - `PerformanceBarPlacementContractTests`（Core，9 条）：启动恢复、临时位置不覆盖、拖动结算+防抖保存、防抖期间改设置合并、退出 flush、相同结算不重摆不保存、显示器变化重新落位、显示器消失夹回、销毁 flush。全部走 `StartupShellController` 合同 + 假端口/假显示器源。
  - `WindowsDisplayEnvironmentSourceTests`（Windows.Tests，3 条集成）：真实枚举 ≥1 台显示器且工作区在边界内、重复枚举一致、订阅后释放幂等。
- 合并 `codex/wpf-rewrite-integration`（4b91396）后全量：Core 81 通过，Windows 17 通过。

**提交**

- `f4c24ee` 实现 F07 性能条贴边、多显示器与位置恢复
- `c2d9cf6` 合入集成分支：解决 UpdateSettings 中自启与位置合并的冲突

**未能自动验证（需人工复验）**

自动化无法覆盖真实桌面交互，请按以下步骤人工复验：

1. 四边四角贴边：真实桌面拖动小窗到上/下/左/右边缘与四个角，松手后应吸附且可视卡片留约 1 DIP 边框；从边缘向屏幕内拖超过约 8 DIP 松手后解除贴边；已贴边状态下向屏幕外拖应弹回。
2. 多显示器：接第二台（不同 DPI 更佳），拖到副屏边缘应吸附到副屏工作区；副屏负坐标区域正常。
3. 显示器插拔：拖到副屏后拔掉副屏（或更改分辨率/缩放），小窗应自动夹回剩余显示器可见工作区，且重启后位置不丢。
4. 重启恢复：贴边状态下退出应用再启动，小窗应恢复到贴边位置；重启期间若工作区变化（如任务栏移动）应重新校正。
5. DPI 变化：系统缩放滑块调整后（部分机器伴随分辨率变化事件）观察小窗是否重新落位；若缩放调整不触发 `WM_DISPLAYCHANGE`/`WM_SETTINGCHANGE`，可重启应用验证恢复路径。
