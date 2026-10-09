# 19: 主占用百分比与时间同色

Status: resolved

## 问题与范围

CPU、内存、GPU 主占用百分比原为绿、蓝、绿，与时间颜色不同。用户于 2026-10-09 确认采用推荐方案：三项有效读数统一跟随时间正文色，深色主题为 `#F3F5F7`，浅色主题为 `#1A1D22`；缺失值继续灰显。显存百分比已经与时间同色，温度、标签、图标与网络箭头保持原样。

## 验收

- [x] 三项有效读数共享时间正文画刷，缺失值仍使用弱化画刷。
- [x] 删除不再使用的三项彩色画刷。
- [x] 现有应用测试及 Release 构建通过。
- [x] 桌面确认深色、浅色和跟随系统主题的实际显示。

## Comments

### 2026-10-09：范围确认

- 用户回复“按你推荐的”，同意主占用百分比跟随时间颜色。
- 这是可逆的局部外观调整，沿用现有术语与主题规则；无需新增词汇表条目或 ADR。

### 2026-10-09：实现与验证

- `PerformanceBarViewModel` 三项有效主占用读数改为使用 `ForegroundBrush`，保留缺失值 `MissingBrush`；删除三项已无用途的彩色画刷。
- 更新现有深浅主题测试，覆盖三项有效读数、GPU 缺失值和保留读数时切换主题。
- `dotnet test tests/PerfMonitor.App.Tests/PerfMonitor.App.Tests.csproj --no-restore --verbosity minimal`：21 项通过。
- `dotnet build src/PerfMonitor.App/PerfMonitor.App.csproj -c Release --no-restore --verbosity minimal`：0 警告、0 错误。
- `git diff --check` 通过；未替换正在运行或已安装的应用，实际桌面外观及系统主题切换待人工复验。

### 2026-10-09：生成手动安装包

- 按用户要求执行 Release / win-x64 依赖框架发布至 `publish/fdd`，再用 Inno Setup 6 编译 `installer/perfmonitor.iss`，均成功。
- 安装包：`installer/Output/PerfMonitorWpf-1.0.0-setup.exe`，包含本事项的百分比颜色修改；发布目录的应用 DLL 与本次 Release / win-x64 构建产物 SHA-256 一致。
- 未执行安装，交由用户手动安装。


### 2026-10-10：用户人工验收通过

- 用户明确确认‘除了内存我已全部验证通过’。本事项非内存验收已通过，状态更新为 `resolved`。
- 证据为用户整体人工确认，详见 [验收记录](../acceptance-2026-10-10.md)；未补写逐项操作过程或测量数据。正文及旧 Comments 中‘待人工验收/未验证’等表述保留为历史情况，以本次确认更新当前结论。
