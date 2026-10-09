# 18: 修复开机自启始终显示保存失败

Status: resolved

## 问题与范围

设置窗开启开机自启时开关回滚，显示「保存失败」。仅修复计划任务适配器的命令参数，遵循 ADR 0005 的当前用户登录触发、最高权限、自有任务名及 Debug 不操作系统自启的约定。

## 验收

- [x] 开启时使用合法的 `/create`，随后 `/query` 确认实际状态。
- [x] 关闭时使用合法的 `/delete`，随后 `/query` 确认实际状态；重复关闭保持幂等。
- [x] 无管理员权限即可运行回归测试，不改动系统任务。
- [x] 全量测试与 Release 构建通过。
- [x] 在提权 Release 实例中操作设置窗，确认开启显示「已保存」且任务存在，关闭后任务不存在。
- [x] 注销后重新登录，确认按任务定义启动。

## Comments

### 2026-10-08：诊断与修复完成，等待桌面复验

- 根因：`WindowsScheduledTaskAutostart` 向 schtasks.exe 传递 `create`、`query`、`delete`，遗漏操作开关的 `/` 前缀。系统直接执行同样的查询参数报 `Invalid argument/option - 'query'`。因此创建失败，后续查询也失败；关闭路径还可能将查询错误误判为任务不存在。
- 最小复现：`dotnet test tests/PerfMonitor.Windows.Tests/PerfMonitor.Windows.Tests.csproj --filter FullyQualifiedName~Enable_and_disable_use_valid_schtasks_operation_switches --verbosity minimal`。修复前 Expected Enabled / Actual Failed；修复后通过。测试通过注入命令行替身运行真实适配器，验证创建、存在查询、删除、删除后查询和重复关闭，临时文件在 finally 中清理。
- 修复：仅补齐三个操作的 `/` 前缀，保持既有系统实际状态回写与保存失败回滚流程。
- `dotnet test PerfMonitor.sln --no-restore --verbosity minimal`：Core 161、Windows 53、App 21，共 235 项通过。Windows 真实任务集成测试因当前测试进程未提权而提前返回，测试框架仍计为通过；该项不代表真实任务验收。
- Release 构建：0 警告、0 错误。修复版另行发布到 `publish/autostart-fix`，避免覆盖正在运行的旧实例文件。
- 应用日志显示当前旧实例 PID 11428 已提权；命令行未提权。当前运行实例尚未替换，桌面开关操作、最高权限任务注册及注销重登尚未验证。
- 使用修复版前从托盘退出旧实例，运行 `publish/autostart-fix/PerfMonitor.App.exe` 并批准 UAC，再开启自启。任务将保存修复版可执行文件路径，应保留完整发布目录。


### 2026-10-10：用户人工验收通过

- 用户明确确认‘除了内存我已全部验证通过’。本事项非内存验收已通过，状态更新为 `resolved`。
- 证据为用户整体人工确认，详见 [验收记录](../acceptance-2026-10-10.md)；未补写逐项操作过程或测量数据。正文及旧 Comments 中‘待人工验收/未验证’等表述保留为历史情况，以本次确认更新当前结论。
