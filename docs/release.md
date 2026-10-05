# 发布说明

产品：性能小窗（PerfMonitor WPF 版）
版本：1.0.0（AssemblyVersion/FileVersion 1.0.0.0）
构建日期：2026-10-05
基线提交：7830f5e（codex/wpf-release-14 分支）

## 运行前提

- Windows 11 x64。
- .NET Desktop Runtime 10.0（验证机装有 Microsoft.WindowsDesktop.App 10.0.12）。发布方式为「依赖框架」，机器上必须已安装运行时；未安装时请先用下方自包含命令发布。
- Release 版首次启动：进程以普通权限（manifest `asInvoker`）启动，若尚未提权会自动发起一次 runas 提权尝试（UAC 弹窗），用于读取 CPU 温度。
  - 批准：提权实例接管，成为主实例。
  - 拒绝或失败：当前普通权限实例继续运行，温度显示为 `--`。同一启动过程不重试。
  - 普通权限与提权实例通过单实例机制交接，不会双开。
- Debug 构建（`dotnet build` 后的 bin/Debug 产物）不弹 UAC、不修改系统自启，仅供开发调试。

## 诊断日志

启动决策与退出原因会追加写入 `%LOCALAPPDATA%\PerfMonitorWpf\app.log`（每行 `HH:mm:ss.fff` 前缀），用于定位「启动后闪退」类问题——优雅退出不留崩溃转储，只能靠过程日志还原链路。记录内容：

- 启动：进程 pid、是否已提权、命令行参数、单实例启动决策（`StartCurrentInstance` / `StartCurrentInstanceAndAttemptElevation` / `HandOffToElevatedInstance` 等）。
- 提权交接：拉起提权实例、提权实例请求接管（原实例随之退出）、接管成功/失败。
- 退出：小窗窗口被关闭、系统会话结束（注销/关机）、托盘/菜单退出、已有实例在运行，以及最终退出码。

排查「闪退」时按 pid 串联：普通权限实例的「退出」若紧跟「提权接管成功」属正常交接；提权实例若无任何触发源日志就退出，则需对照代码中全部退出调用点排查。日志为多实例并发追加，单条写入失败即丢弃，不影响主流程。

## 构建命令

依赖框架（默认发布方式，产物约 0.9 MB）：

```
dotnet publish src/PerfMonitor.App/PerfMonitor.App.csproj -c Release -r win-x64 --self-contained false -o publish/fdd
```

自包含（无需安装运行时，产物约 173 MB，备选）：

```
dotnet publish src/PerfMonitor.App/PerfMonitor.App.csproj -c Release -r win-x64 --self-contained true -o publish/sc
```

发布方式选择依据（实测 2026-10-05，SDK 10.0.401）：

- 自包含 173 MB / 依赖框架 0.9 MB，两者均发布成功且静态验证等价（PE Subsystem=2、asInvoker、图标与版本信息一致）。
- 本机与自用目标机已确认安装 .NET 10 Desktop Runtime，依赖框架即满足「可运行」要求，体积小约 200 倍，故选依赖框架；自包含命令作为运行时缺失时的备选。
- 单文件（`-p:PublishSingleFile=true`）与裁剪（`-p:PublishTrimmed=true`）：**未采用**（未做启动实测；WPF 单文件/裁剪存在已知兼容风险，自用场景无此需求）。
- 不引入安装器（规格既定）。

## 权限说明汇总

| 构建配置 | manifest | 启动行为 |
| --- | --- | --- |
| Debug | asInvoker | 普通权限直接运行，不发起提权尝试 |
| Release | asInvoker | 未提权时自动发起一次 runas 提权尝试；拒绝则普通权限运行，温度显示 `--` |

## 开机自启与清理

开机自启通过任务计划程序实现，任务名为 `PerfMonitorWpf`（当前用户登录触发、最高权限运行）。应用内设置窗开关即可创建/删除。

手动查询与清理（仅在需要时使用；新版只操作 `PerfMonitorWpf` 这一个任务，不会触碰旧版或其他任务）：

```
schtasks /Query /TN PerfMonitorWpf
schtasks /Delete /TN PerfMonitorWpf /F
```

卸载应用前，先在设置窗中关闭自启，或执行上面的删除命令。

## 干净启动人工复验步骤（自动化未覆盖）

1. 确认系统无 `PerfMonitorWpf` 计划任务残留（`schtasks /Query /TN PerfMonitorWpf` 应报不存在）。
2. 双击发布目录中的 `PerfMonitor.App.exe` 启动（会弹一次 UAC）。
3. 托盘图标出现且随主题切换（亮/暗），左键菜单各项可用。
4. 性能条显示 CPU/内存/GPU/网络，提权后 CPU 温度有数值（拒绝 UAC 的会话中应为 `--`）。
5. 再次启动 exe：不出现第二个实例，已有窗口激活/接管。
6. 设置窗：打开、修改刷新率等设置、关闭后隐藏；再次打开状态保持。
7. 在设置窗开启开机自启，`schtasks /Query /TN PerfMonitorWpf` 应存在；关闭后应消失。
8. 托盘菜单退出：性能条、托盘图标消失，进程结束（任务管理器无残留），计划任务按用户选择保留或删除。

## 已验证（自动化/静态，2026-10-05）

- 两种发布方式产物生成成功，发布无警告；`publish/fdd` 文件齐全（exe、deps.json、runtimeconfig.json、Core/Windows dll、Resources 资源目录）。
- PE 头：Machine=0x8664（x64）、Subsystem=2（Windows GUI，无控制台）。
- 内嵌 manifest：`requestedExecutionLevel level="asInvoker"`，无 requireAdministrator/highestAvailable，含 PerMonitorV2 DPI 声明。
- 版本信息（Win32 VerQueryValue 实读）：FileDescription=性能小窗（PerfMonitor WPF 版）、FileVersion=1.0.0.0、ProductName=性能小窗、Copyright © 2026。
- 应用图标：icon.ico（16/24/32/48/256 五尺寸）通过 ApplicationIcon 嵌入 exe 资源段（内嵌 5 个 PNG 图像）。
- Debug 构建 smoke：进程启动、6 秒存活、优雅退出正常。
- 全量测试：47 + 139 = 186 项通过，0 失败。
