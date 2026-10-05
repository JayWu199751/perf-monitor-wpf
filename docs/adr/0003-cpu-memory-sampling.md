# CPU 与物理内存采样口径

> 状态：Accepted

## 决策

- Windows adapter 动态枚举所有活动 processor group 的活动处理器掩码，并在调用线程上逐组设置有效的单处理器 affinity 后调用 `GetSystemTimes`。将每组 kernel、user、idle 计数分别以 checked arithmetic 累加，最后在 `finally` 恢复原 affinity；任一组枚举、切换、读取、求和、恢复失败，或一次采样前后拓扑变化，整条 CPU 读数返回缺失，不能以部分组的和建立差分。`GetSystemTimes` 的 kernel 已包含 idle，CPU 使用率按 `(Δkernel + Δuser - Δidle) / (Δkernel + Δuser)` 计算。CPU 快照携带活动组数；相邻样本组数变化时本轮丢弃差分并以新样本重建基线。每次开始可见采样时，首个有效读数建立新基线并显示 0%。
- 物理内存来自 `GlobalMemoryStatusEx`。先在原始字节上计算已用比例，再以 JavaScript `Math.round` 相同的非负中点向上规则取整数；快照另保留已用与总量 GiB。
- CPU 与内存共用一个快通道，默认 1000 ms，只接受 1000、2000、5000 ms。一次读取完成后才等待下个周期，stop 使用取消令牌，跨 restart 使用采样代际隔离；停止期间不能把迟到结果投递给性能条。
- 自动化行为经 `StartupShellController` 这一用户可见合同接缝验证。Windows adapter 由 Windows 集成测试直接读取本机 API；App 层仅将不可变快照投递到 Dispatcher，并再次检查代际。

## 验收边界

`GetSystemTimes` 在超过 64 个逻辑处理器时只返回调用线程主 processor group 的计数，因此 adapter 会逐组设置主组、读取再聚合，不对 processor group 数量硬编码上限。当前 Windows 验收主机由 `GetActiveProcessorGroupCount` 报告 1 组；Windows.Tests 验证本机 API 读数与调用线程 affinity 恢复。当前主机未实测多组硬件路径，该项仍需在多组 Windows 主机补充硬件验收；这不是产品支持限制。

## 依据

正式规格 `.scratch/wpf-rewrite/spec.md` 的 F02、F05，架构建议 `rewrite-wpf/architecture.md` 的数据源和生命周期边界，以及验收项 A04、A05、A13、A14。API 行为依据 [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes)、[SetThreadGroupAffinity](https://learn.microsoft.com/en-us/windows/win32/api/processtopologyapi/nf-processtopologyapi-setthreadgroupaffinity) 与 [GetLogicalProcessorInformationEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getlogicalprocessorinformationex)。
