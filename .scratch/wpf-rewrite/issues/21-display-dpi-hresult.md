# 21：修复显示器 DPI 查询的 HRESULT 处理

Status: resolved
Blocked by: 无

## 问题

2026-10-09 短时验收发现：本机真实窗口 DPI 为 144（150%），`WindowsDisplayEnvironmentSource` 却输出 `DpiScale=1.0`。同显示器句柄的原生调用返回 `HRESULT=0`、`dpiX=dpiY=144`；现有 `bool` 声明返回 false，导致缩放比例回退。

根因位置：`src/PerfMonitor.Windows/Native/NativeMethods.cs` 的 `GetDpiForMonitor` 返回值声明，以及 `src/PerfMonitor.Windows/Displays/WindowsDisplayEnvironmentSource.cs` 的成功判断。可能影响贴边阈值、安全边距及其他消费显示器缩放比例的布局行为。

## 复现与证据

见 [2026-10-09 验收报告](../acceptance-2026-10-09.md)。本机差分命令已两次返回退出码 1：

```powershell
dotnet run --project out/acceptance-20261009/RuntimeProbe/RuntimeProbe.csproj -c Release --no-build -- 'C:\Users\10854\Code\PerfMonitor-WPF\out\acceptance-20261009' --check-dpi
```

原生结果 `0/144/144`，adapter `false/144/144`，模块比例 `1.0`，期望 `1.5`。既有测试只检查比例不低于 1.0，未覆盖其正确性。

## 建议修复范围

- 正确声明 HRESULT 并检查成功/失败语义，核对全部调用点。
- 增加能在修复前失败的回归覆盖；不能仅断言比例大于等于 1。
- 考虑 API 对 DPI awareness 的约束，保持 PerMonitorV2 应用的显示器几何口径一致。
- 在本机 150% DPI 复验原生值、模块值、贴边阈值；混合 DPI 多屏无硬件时继续标未验证。

## 验收

- [x] 原生查询成功时输出正确比例（本机 1.5），失败时安全回退。
- [x] 回归测试能识别此次成功返回值被误判的缺陷。
- [x] 相关 Windows 测试与 Release 构建通过。
- [x] 贴边和位置恢复的实际桌面效果复验，未执行部分明确记录。

## Comments

- 2026-10-09：验收发现并稳定复现；本轮只登记，未修改产品实现。

### 2026-10-09：用户授权修复，自动化与本机差分复验通过

- 将共享 `GetDpiForMonitor` 返回类型改为 `int`（HRESULT），显示环境模块与设置窗工作区换算两处调用统一以 `== 0` 判断成功，并保留零 DPI/查询失败的回退。
- 修复前先运行新 Windows 回归测试，明确失败：Expected 1.5 / Actual 1。修复后通过；另覆盖无效显示器查询回退到 1.0。
- 新增真实 WPF 设置窗测试，通过独立的 `GetDpiForWindow` 计算预期工作区 DIP，验证设置窗最大高度，覆盖第二处调用。
- `dotnet test PerfMonitor.sln --no-restore`：Core 158、Windows 58、App 19，共 235 项通过，0 失败、0 跳过。TRX 位于 `out/issue21-verification/`。
- `dotnet build PerfMonitor.sln -c Release --no-restore`：0 警告、0 错误；Release / win-x64 发布到 `publish/fdd` 成功。
- 原验收差分命令在重新发布后通过：HRESULT=0，dpiX=dpiY=144，adapter 返回 0，期望/模块比例均为 1.5；同一探针的两代设置窗均通过。新证据保存在 `out/issue21-verification/dpi-repro.json` 与 `runtime-probe.json`，保留原验收目录的失败证据。
- 现有 Core 贴边测试随全量回归通过，覆盖 150% 时 12 px 内贴边、13 px 外不贴边、安全边距 1.5 px。该合同测试不代替真实鼠标拖动；本机只有单屏，混合 DPI/多屏仍未验证。
- 实现已完成；状态为 `ready-for-human`，等待实际桌面贴边、位置恢复和短屏滚动复验。本轮未生成新安装包或改写已安装副本。

### 2026-10-09：生成包含 DPI 修复的安装包

- 按用户要求执行 Release / win-x64 依赖框架发布及 Inno Setup 6 编译，均成功。为避免覆盖运行中的 `publish/fdd`，使用独立的 `publish/package-issue21` 目录，并生成仅替换发布/输出目录的临时安装脚本；仓库安装脚本未改动。
- App、Core、Windows 三个发布程序集与本次 Release / win-x64 构建的 SHA-256 均一致；编译日志确认打入独立发布目录的文件。
- 安装包：`C:\Users\10854\Code\PerfMonitor-WPF\installer\Output\PerfMonitorWpf-1.0.0-setup.exe`。
- 大小：2294437 字节；SHA-256：`CE6213692ADF2A575AB13FE91B82A303425D069979A72C1B568976263ACFE439`。
- 包含事项 21 的显示环境与设置窗 DPI 修复；依赖 .NET Desktop Runtime 10.0。未执行安装，运行中的应用保持不变。
- 打包脚本、编译日志和程序集校验记录位于 `out/package-issue21/`。


### 2026-10-10：用户人工验收通过

- 用户明确确认‘除了内存我已全部验证通过’。本事项非内存验收已通过，状态更新为 `resolved`。
- 证据为用户整体人工确认，详见 [验收记录](../acceptance-2026-10-10.md)；未补写逐项操作过程或测量数据。正文及旧 Comments 中‘待人工验收/未验证’等表述保留为历史情况，以本次确认更新当前结论。
