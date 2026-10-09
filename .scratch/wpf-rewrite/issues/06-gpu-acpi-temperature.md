# 06: GPU 与 ACPI 温度

**What to build:** 用户可看到 NVIDIA GPU 使用率、显存和温度，以及系统可用时的 ACPI 温度；任何单项不可用都不会伪造读数或中断其他指标。

**Blocked by:** 03: Release 提权与跨权限单实例；04: CPU 与内存指标。

**Status:** resolved

## 验收检查

- [x] NVIDIA adapter 使用约定字段、无控制台异步子进程、5 秒超时和首条有效记录解析；Windows 集成测试在本机读到单卡真实数据。
- [x] 多卡首条有效记录、命令不存在、非法多行输出及运行中超时/取消回收尚未逐项实测；没有在本机伪造硬件或故障。
- [x] GPU、ACPI 温度由独立慢通道采样，快照以同一代际合并；任一慢源抛错/缺失不会覆盖另一慢源或 CPU、内存。
- [x] 慢刷新默认 3000 ms，只接受 3000/5000 ms；Core 合同验证初始快照、双向失败隔离、取消、代际隔离和不重叠采样。
- [x] ACPI adapter 查询 `root\WMI` 热区并执行十分之一 Kelvin 到摄氏整数换算，按最大有效热区值返回；查询失败或无有效数据返回缺失。
- [x] 本机 ACPI 查询返回缺失，成功读数、多热区、拒绝访问与具体缺失原因未验证。
- [x] 性能条展示 GPU 使用率、显存使用率、GPU 温度与 ACPI 热区温度；所有只读 `Run.Text` 绑定显式使用 `Mode=OneWay`。

## Comments

- 2026-10-05：基于集成提交 `3978b8f` 的独立分支 `codex/wpf-gpu-temperature-06` 实施。App 仅组装 `WindowsSlowMetricsSource` 与 `StartupShellController`；GPU/WMI adapter 位于 `PerfMonitor.Windows`。
- Core 行为自动化仅经 `StartupShellController`：验证 GPU/ACPI 读数并入用户可见快照、两种慢源故障互不影响且快通道保持更新、3000/5000ms 周期、隐藏后取消与迟到代际丢弃、慢读取不重叠。Windows 集成测试实际调用本机 adapter，并检查返回值范围及调用前取消。
- TDD 红测证据：慢通道实现前，定向 Controller 合同测试在 3 秒等待后因没有慢指标快照而失败（`OperationCanceledException`）；实现后该合同与其余 Core 合同通过。
- GPU adapter 通过固定参数运行无控制台 `nvidia-smi`，超时/取消后终止进程树并回收输出；逐行选择第一条有效 CSV 记录。ACPI adapter 采用异步 WMI observer；查询超时 5 秒，取消后清理等待有界，未完成的 WMI 查询不会与后续查询重叠。详情见 [ADR 0004](../../../docs/adr/0004-gpu-acpi-slow-metrics.md)。
- 本机 Windows 集成实测：`nvidia-smi` 返回 GPU 使用率 37%、显存占用率 45%、GPU 温度 80°；CPU ACPI 热区查询返回缺失，原因无法确定（可能为权限、固件未公布热区或无可读数据）。因此 ACPI 成功换算、多热区最大值、访问拒绝及 GPU 多卡、命令缺失/超时/取消路径不得视为已验证。
- 复验步骤：在有多个 NVIDIA GPU 的 Windows 主机上检查多行 CSV 首条有效行；分别将 `nvidia-smi` 从 PATH 移除、提供非法输出或用可控长时间子进程模拟超时/取消，并确认进程退出且 CPU/内存/网络继续更新；在可访问 ACPI 热区的主机记录原始热区结果并确认最高有效值；另以非管理员与拒绝访问条件复验缺失态。这些硬件/故障路径仍需按步骤复验。
- 合入集成 tip `ce572af` 后，已复核 network/CPU-group 共享合同；Controller 合同验证慢通道更新保留当前双向网速，快通道更新保留已有 GPU 与温度。Debug WinExe 实际启动运行 8 秒以上，随后对该进程窗口发送 `WM_CLOSE`，进程以退出码 0 退出。
- 最终复验：`dotnet test PerfMonitor.sln --no-restore --verbosity minimal`，Core.Tests 38/38、Windows.Tests 5/5 通过；Debug 和 Release solution build 均 0 警告、0 错误。


### 2026-10-10：用户人工验收通过

- 用户明确确认‘除了内存我已全部验证通过’。本事项非内存验收已通过，状态更新为 `resolved`。
- 证据为用户整体人工确认，详见 [验收记录](../acceptance-2026-10-10.md)；未补写逐项操作过程或测量数据。正文及旧 Comments 中‘待人工验收/未验证’等表述保留为历史情况，以本次确认更新当前结论。
