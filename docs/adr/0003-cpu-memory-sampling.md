# CPU 与物理内存采样口径

> 状态：Accepted

## 决策

- Windows adapter 通过 `GetSystemTimes` 取得全机累计 kernel、user、idle 时间。kernel 已包含 idle，CPU 使用率按 `(Δkernel + Δuser - Δidle) / (Δkernel + Δuser)` 计算；计数差分异常或读取失败时返回缺失。每次开始可见采样时，首个有效读数建立新基线并显示 0%。
- 物理内存来自 `GlobalMemoryStatusEx`。先在原始字节上计算已用比例，再以 JavaScript `Math.round` 相同的非负中点向上规则取整数；快照另保留已用与总量 GiB。
- CPU 与内存共用一个快通道，默认 1000 ms，只接受 1000、2000、5000 ms。一次读取完成后才等待下个周期，stop 使用取消令牌，跨 restart 使用采样代际隔离；停止期间不能把迟到结果投递给性能条。
- 自动化行为经 `StartupShellController` 这一用户可见合同接缝验证。Windows adapter 由 Windows 集成测试直接读取本机 API；App 层仅将不可变快照投递到 Dispatcher，并再次检查代际。

## 限制

`GetSystemTimes` 在超过 64 个逻辑处理器时只覆盖调用线程所在的 processor group。本实现按常见桌面规模提供全机读数，不宣称对多 processor group 机器聚合了所有处理器；若目标机器超过该边界，再单独设计和验证聚合方式。

## 依据

正式规格 `.scratch/wpf-rewrite/spec.md` 的 F02、F05，架构建议 `rewrite-wpf/architecture.md` 的数据源和生命周期边界，以及验收项 A04、A05、A13、A14。
