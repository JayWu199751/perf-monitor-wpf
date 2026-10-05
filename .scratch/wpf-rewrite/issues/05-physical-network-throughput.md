# 05: 物理网卡网速

**What to build:** 用户看到的上下行速度尽量反映真实物理链路，并避免把 TUN、Hyper-V 等重复接口累加成虚高流量。

**Blocked by:** 04: CPU 与内存指标。

**Status:** ready-for-human

- [x] 仅将处于 Up 状态且非 loopback 的接口作为基础候选；优先使用 PhysicalMediumType 不为 Unspecified 的物理接口，无物理候选时回退到基础候选。
- [x] 每轮选择收发速率之和最大的一行作为上下行来源；不得累加子接口，也不得分别挑选上下行最大值。
- [x] 使用单调时钟实测采样间隔计算字节每秒，再按 1024² 换算并显示一位小数；下行和上行来自同一接口。
- [x] 首轮、无基线或非正间隔显示 0；计数回退只将该方向本轮置 0；读取失败显示 --。
- [x] 快速刷新频率与 CPU/内存一致；筛选变化不丢弃有效的接口基线。

## Comments

### 2026-10-05 实施与验证

- Core 以 `StartupShellController` 为唯一自动化行为接缝；同一快通道顺序读取 CPU、内存、网络，并由单个采样调度器暂停、恢复和按代际隔离。
- 网络合同测试覆盖 Up/非 loopback 基础筛选、物理介质优先、无物理候选时回退、Down 与 loopback 排除、收发总速率最高的一行、筛选切换时保留虚拟候选基线、新接口首轮零基线、方向性计数回退、非正单调间隔、读取缺失、失败恢复后按真实时间差、二进制 MiB/s 换算和一位小数中点向上。快通道 1000/2000/5000ms 仍由既有 Controller 合同测试覆盖。
- Windows adapter 调用 `GetIfTable2Ex(MibIfTableNormal)`，并在 `finally` 中调用 `FreeMibTable`；网络快照时间取自 `Stopwatch` 单调计时器。性能条网络段绑定为同一行的蓝色 `↓`、`↑` 及一位小数 `MB/s`，缺失时分别显示 `--`。
- `dotnet test PerfMonitor.sln --verbosity minimal`：Core.Tests 20/20、Windows.Tests 1/1 通过。Windows 集成测试在本机读取了接口表，检查接口记录、LUID 与单调时间戳。
- `dotnet build PerfMonitor.sln --no-restore --verbosity minimal`：成功，0 警告、0 错误。
- 尚未在真实性能条窗口中进行持续上传/下载、TUN/Hyper-V 并存或拔插/重置网卡的桌面验收；Core 算术使用替身输入，Windows 测试验证本机 API 结构读取而非速率真实性。复验步骤：运行应用并产生可控双向流量，确认网络段方向、单位和一位小数；在物理网卡与 TUN/Hyper-V 同时启用时观察是否只采用一行且没有重复累加，再停用物理候选确认回退行为。
- 集成审查后为基线字典增加了成功接口表代际清理：每次完整读取后只保留当前 Up 且非 loopback 的 LUID，介质筛选变化仍更新并保留基线；空完整表会清除全部基线，读取缺失则不清基线。新增 Controller 合同回归测试先以旧值 2.0 MB/s 失败，再以重现接口的 0 MB/s 新基线通过。修正后复跑：Core.Tests 20/20、Windows.Tests 1/1，解决方案构建 0 警告、0 错误。
