# 04: CPU 与内存指标

**What to build:** 用户能在性能条中看到及时更新的全机 CPU 使用率和内存使用率，读取失败时能区分缺失与真实的零值。

**Blocked by:** 01: 应用启动壳与托盘入口。

**Status:** ready-for-human

- [x] CPU 使用率由全机累计时间差分得到，首次基线为 0，结果为 0–100 整数；读取失败为缺失。
- [x] 内存百分比使用原始物理字节计算，界面显示整数；快照保留已用和总量 GiB。
- [x] CPU 和内存读取不阻塞界面；快速刷新可选 1000/2000/5000ms、默认 1000ms，同一通道采样不重叠。
- [x] 重启或停止采样后，较早的在途结果不能覆盖新结果或投递到已退出的界面。
- [x] 百分比采用与旧版一致的 Math.round 中点语义；缺失数据显示为 --，不伪装为 0。

## Comments

- 2026-10-05：从集成提交 `198ac7c` detached HEAD 建立 `codex/wpf-cpu-memory-04`。新增 `PerfMonitor.Windows` 并接入 `PerfMonitor.App`；`App.xaml.cs` 与 `PerformanceBarWindow.xaml.cs` 未修改。性能条 CPU/内存段改为绑定实时 ViewModel，GPU/网络仍保持占位，不在本票范围内。
- Core 自动化只通过 `StartupShellController` 合同接缝：验证首轮 CPU 0、kernel 含 idle 的差分与 75.5→76 中点舍入、原始字节内存比例、GiB 字段、源失败缺失、刷新周期范围，以及隐藏/重显期间在途结果代际隔离和串行读取。
- Windows 集成测试使用当前 Windows 实机调用 `GetSystemTimes` 与 `GlobalMemoryStatusEx`，验证结构、单位输入与返回数据关系。具体取舍见 [ADR 0003](../../../docs/adr/0003-cpu-memory-sampling.md)。`GetSystemTimes` 对超过 64 个逻辑处理器的 processor group 边界仍适用；本实现没有跨组聚合。
- 自动化证据：`dotnet build PerfMonitor.sln --no-restore`（0 警告、0 错误）；`dotnet test PerfMonitor.sln --no-restore`（Core.Tests 16 项、Windows.Tests 1 项全部通过）。WPF XAML 已构建；透明窗口上的真实显示与刷新仍需人工桌面验收。
