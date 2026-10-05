# 04: CPU 与内存指标

**What to build:** 用户能在性能条中看到及时更新的全机 CPU 使用率和内存使用率，读取失败时能区分缺失与真实的零值。

**Blocked by:** 01: 应用启动壳与托盘入口。

**Status:** ready-for-human

- [x] CPU 使用率聚合所有活动 processor group 的累计时间后差分，首次基线为 0，结果为 0–100 整数；读取失败为缺失。
- [x] 内存百分比使用原始物理字节计算，界面显示整数；快照保留已用和总量 GiB。
- [x] CPU 和内存读取不阻塞界面；快速刷新可选 1000/2000/5000ms、默认 1000ms，同一通道采样不重叠。
- [x] 重启或停止采样后，较早的在途结果不能覆盖新结果或投递到已退出的界面。
- [x] 百分比采用与旧版一致的 Math.round 中点语义；缺失数据显示为 --，不伪装为 0。

## Comments

- 2026-10-05：从集成提交 `198ac7c` detached HEAD 建立 `codex/wpf-cpu-memory-04`。新增 `PerfMonitor.Windows` 并接入 `PerfMonitor.App`；`App.xaml.cs` 与 `PerformanceBarWindow.xaml.cs` 未修改。性能条 CPU/内存段改为绑定实时 ViewModel，GPU/网络仍保持占位，不在本票范围内。
- Core 自动化只通过 `StartupShellController` 合同接缝：验证首轮 CPU 0、kernel 含 idle 的差分与 75.5→76 中点舍入、原始字节内存比例、GiB 字段、源失败缺失、刷新周期范围，以及隐藏/重显期间在途结果代际隔离和串行读取。
- 2026-10-05 修正：Windows adapter 使用 `GetLogicalProcessorInformationEx` 动态枚举活动组与有效 CPU mask，逐组用 `SetThreadGroupAffinity` 切换调用线程并读取 `GetSystemTimes`，checked 累加三类时间，在 `finally` 恢复原 affinity。任一组读取/恢复失败或采样前后拓扑变化时返回缺失；活动组数变化时 Core 丢弃跨拓扑差分并重建基线，不限制产品支持的组数。详见 [ADR 0003](../../../docs/adr/0003-cpu-memory-sampling.md)。
- 2026-10-05 Windows 集成实测：当前主机 `GetActiveProcessorGroupCount` 为 1；`Windows.Tests` 的 2 项测试通过。一次 `GetSystemTimes` 聚合样本（100ns）为 kernel `1277789687500`、user `179226093750`、idle `1148807187500`；调用线程 affinity 恢复为 group 0 / mask `0xFFFF`。当前主机未实测多组硬件聚合路径，需在多组 Windows 主机补充验收；这不是产品限制。
- 2026-10-05 Core Controller 合同回归：通过 `StartupShellController` 验证相邻样本活动组数变化时该轮 CPU 缺失，下一轮以新组集合重建基线；临时移除 group-count guard 时回归按预期失败并错误显示 20%，恢复后通过且重建样本显示 60%。
- 2026-10-05 事项03 Debug smoke 反馈：真实 Debug smoke 曾发现性能条 XAML 的 `Run.Text` 绑定默认采用 TwoWay，导致只读属性绑定异常；事项03 分支改为显式 `OneWay` 后复验主实例稳定，双启动与关闭通过。此 UI 回归修正在事项03 集成分支，需随最新 integration 同步。
- 初始实现自动化证据：`dotnet build PerfMonitor.sln --no-restore`（0 警告、0 错误）；`dotnet test PerfMonitor.sln --no-restore`（Core.Tests 16 项、Windows.Tests 1 项全部通过）。本次 processor-group 修正后完整验证：`dotnet build PerfMonitor.sln --no-restore`（0 警告、0 错误）；`dotnet test PerfMonitor.sln --no-restore`（Core.Tests 18 项、Windows.Tests 2 项全部通过）。透明窗口真实显示与刷新仍需人工桌面验收。
