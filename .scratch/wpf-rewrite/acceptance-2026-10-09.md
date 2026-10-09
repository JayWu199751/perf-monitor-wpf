# 2026-10-09：构建、测试与短时运行验收

> 当前状态更新（2026-10-10）：用户确认「除了内存我已全部验证通过」，非内存事项已关闭，详见[用户验收记录](acceptance-2026-10-10.md)。下文为历史验收证据，F13 内存验收仍未完成。

> 后续修复复验：用户随后授权修复事项 21。HRESULT 声明及显示环境/设置窗两处成功判断已修正，235 项测试与 Release 构建通过；本机差分结果为原生 144 DPI、模块比例 1.5。详见 [事项 21 的修复记录](issues/21-display-dpi-hresult.md)。下文保留修复前的原始验收结果；混合 DPI/桌面交互和 F13 仍未验证。

## 结论与范围

**本轮部分通过，发现一项可稳定复现的 DPI 缺陷；不能宣布整体验收通过。**

用户选择「先完成构建、测试和短时运行验收」。本轮没有执行六轮新旧版配对、一小时稳态、50 轮设置和 20 轮显隐长时验证。F13 仍为「未验证」。

基线：`c785b54dd9a8dc8ff360da11152856c0c4466e99`，分支 `codex/wpf-rewrite-integration`。Windows 11 专业版 `10.0.26300`；PowerShell `7.6.6`；.NET SDK `10.0.401`。实际窗口 DPI 为 144（150%），本机枚举到一台显示器。所有命令均使用 PowerShell 7。

## 自动化与发布版

| 检查 | 结果 |
| --- | --- |
| `dotnet test PerfMonitor.sln --no-restore --logger 'trx;LogFilePrefix=acceptance-20261009' --results-directory out/acceptance-20261009 --verbosity minimal` | Core 158、Windows 56、App 18，共 232 项通过，0 失败、0 跳过 |
| `dotnet build PerfMonitor.sln -c Release --no-restore --verbosity minimal` | 0 警告、0 错误 |
| Release / win-x64 依赖框架发布到 `publish/fdd` | 成功 |
| 发布 DLL 与 Release / win-x64 构建 DLL SHA-256 比对 | 一致 |
| `git diff --check` | 通过 |

此前受 Zen 窗口遮挡影响的任务栏置顶回归测试本轮通过。232 项既有测试全过，不代表下面的补充 DPI 验收通过。

## 短时运行与真实窗口元数据

- 发布版以 `--elevation-attempted` 启动为普通权限实例，避免触发 UAC。本轮没有验证正常首次启动的 UAC 批准、拒绝和跨权限交接。
- 主 PID `23664` 持续响应；再次启动 PID `1992` 在 412 ms 内以退出码 0 结束，主 PID 保持不变。日志记录 `NotifyExistingInstanceAndExit`。
- 性能条实际 HWND 扩展样式为 `0x00080088`：工具窗口、分层窗口、置顶；可见且未被 DWM cloaking。没有直接操作 Alt+Tab/任务视图或检查托盘像素。
- 临时运行探针引用本次发布程序集和同一 manifest，在屏幕外创建真实 WPF 设置窗：两代窗口共 10 次开关保存、隐藏与重开，重开复用 HWND，销毁后 `IsWindow=false`；使用独立配置文件，未修改真实用户设置。
- 设置窗实测宽 380 DIP，原生尺寸 570×1037 px，DPI=144；`CornerRadius=12`、`GlassFrameThickness=0`。`GetWindowRgn` 返回复杂区域，四个极角像素均被排除，12 DIP 内部点保留，证实原生窗口区域发生圆角裁切。不能据此代替屏幕上的边缘和阴影观感验收。
- 浅色背景 `#FFF5F5F7`、深色背景 `#FF111113`，system 当前解析为深色。未修改系统主题，没有验证操作系统主题变化通知的真实交互链。
- 时钟独立从 `23:40:56` 更新到 `23:40:57`。CPU 累计计数、物理内存、网络计数读取成功；本机 GPU 样本为 2%、显存 31%、61°C；ACPI 温度为缺失，成功温度路径仍未验证。

运行探针命令：

```powershell
dotnet run --project out/acceptance-20261009/RuntimeProbe/RuntimeProbe.csproj -c Release -- 'C:\Users\10854\Code\PerfMonitor-WPF\out\acceptance-20261009'
```

## 短时内存记录

```powershell
pwsh.exe -NoProfile -File tools/memory-probe.ps1 -ProcessId 23664 -IntervalMs 1000 -DurationSeconds 120 -WarmupRounds 0 -OutputCsv out/acceptance-20261009/wpf-short.csv
```

按父子 PID 每轮重建整进程树，汇总 Private Bytes，MiB=字节/1048576。120 个样本，P95 用最近邻秩法。采样开始时应用已运行约三分钟；没有执行规格中的完整五分钟预热。采样期间另有探针编译负载，亦不满足配对稳态环境要求。

| 均值 MiB | P95 MiB | 峰值 MiB | 最小 MiB |
| --- | --- | --- | --- |
| 86.345 | 86.945 | 92.996 | 84.098 |

采样时点均只观测到主进程；每秒枚举可能漏掉两个时点之间生灭的 `nvidia-smi` 子进程。因此不能把该峰值视为包含所有短命子进程瞬态峰值的严格上界，不能据此宣称 F13 达标或不存在泄漏。

## 未通过：显示器 DPI 数据源

两次差分复验均失败（退出码 1）：

```powershell
dotnet run --project out/acceptance-20261009/RuntimeProbe/RuntimeProbe.csproj -c Release --no-build -- 'C:\Users\10854\Code\PerfMonitor-WPF\out\acceptance-20261009' --check-dpi
```

对同一个原生显示器句柄的结果：

```text
原生 HRESULT=0，dpiX=dpiY=144
现有 P/Invoke 返回 false，但 out dpiX=dpiY=144
期望比例=1.5，WindowsDisplayEnvironmentSource 实际比例=1.0
```

`NativeMethods.GetDpiForMonitor` 的返回类型被声明为 `bool`；API 实际返回 HRESULT，成功的 0 被解释为 false，`GetDpiScale` 因而回退到 1.0。现有枚举测试仅断言 `DpiScale >= 1.0`，未捕获此问题。可能影响按 DIP 换算的贴边阈值和安全边距；本轮没有声称已复现所有相关视觉症状。

依据：[Microsoft GetDpiForMonitor 文档](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)。修复记录见 [事项 21](issues/21-display-dpi-hresult.md)。本轮只诊断并登记，没有修改产品实现。

## 工具限制与保留项

computer-use 能列出其他桌面窗口，但两次枚举以及显式启动重试均未返回本应用可操作窗口；启动工具报告 `launched app did not expose a targetable window`。因此没有执行真实鼠标拖动、托盘点击、菜单、短屏滚动、四角/阴影观感或透明合成的视觉验收。窗口元数据和屏幕外 WPF 探针归为 W 层，不冒充 M 层人工验收。

保留：DPI 缺陷修复后复验、真实桌面视觉与交互、混合 DPI/多屏、UAC 与登录自启、ACPI 成功路径，以及 F13 配对和长时压力验证。

## 原始证据

本机原始文件位于仓库 `out/acceptance-20261009/`：三份 TRX、`environment.json`、`single-instance.json`、`window-styles.txt`、`runtime-probe.json`、`dpi-repro.json`/`.log`、`wpf-short.csv`/`.summary.txt`，以及 `RuntimeProbe/` 的可重跑源代码。

真实用户配置 SHA-256 与验收前备份一致。运行探针所有设置写入均位于独立的 `isolated-settings.json`。

验收结束时，临时运行探针进程均已结束；本次启动的 `publish/fdd/PerfMonitor.App.exe`（PID 23664）保留运行，便于用户查看界面。本轮没有验证该进程从真实菜单退出的清理行为，也没有安装或替换已安装副本。
