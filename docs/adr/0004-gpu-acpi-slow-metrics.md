# GPU 与 ACPI 温度的慢指标采样

> 状态：Accepted

性能条需要在不拖慢 CPU、内存和网络更新的情况下显示 NVIDIA GPU 指标与可用的 ACPI 热区温度。决定由 `StartupShellController` 管理一个可取消的慢指标通道，把结果合并进同一代际的指标快照；GPU 与温度分别失败时只将对应值置为缺失。

## 决策

- 慢指标默认每 3000 ms 采样，只接受 3000 或 5000 ms。GPU 与 ACPI 查询并发执行，但一次慢采样完成前不会开始下一次；隐藏、退出或代际切换会取消当前查询，旧代际结果不发布。
- NVIDIA GPU 由 Windows adapter 通过无控制台 `nvidia-smi` 子进程读取固定查询字段，5 秒超时后终止进程树并回收输出流。逐行解析 CSV，使用首条完整且有效的设备记录；显存占用率按已用量除以总量计算，中点按 `Math.round` 语义向上取整。
- ACPI 温度由 Windows adapter 使用 `System.Management` 异步查询 `root\WMI` 的 `MSAcpi_ThermalZoneTemperature.CurrentTemperature`。原始值按十分之一 Kelvin 换算为摄氏整数，拒绝不在 `(0, 120)` 摄氏范围内的热区并显示最高有效热区值；没有热区、权限拒绝、无效值或查询错误都显示缺失。
- WMI 查询 5 秒后取消；取消后最多等待 1 秒确认完成。若 provider 没有及时发出完成事件，查询门仍保持关闭，避免后续查询与未结束的 WMI 操作重叠；后续调用等待有界并降级为缺失。`ManagementObjectSearcher` 与每条结果对象在已知完成路径释放；`ManagementOperationObserver` 通过 `Cancel()` 取消，类型本身未提供 `IDisposable`。
- Core 自动化合同仅通过 `StartupShellController` 检查用户可见快照、故障隔离、刷新周期、取消与代际隔离。Windows 集成测试实际调用本机 adapter，并允许硬件或权限导致指标缺失。

## 验收边界

当前验收主机通过真实 `nvidia-smi` 返回单设备 GPU 指标；多 NVIDIA 设备中的首条有效记录、无 NVIDIA、命令超时及运行中取消仍需相应硬件/故障注入复验。当前 ACPI 查询没有返回可用热区，无法据此判断是权限不足、固件未公布热区还是系统无可读数据；成功换算、多热区最大值和拒绝访问路径尚未实测。

## 依据

正式规格 F03/F05、事项 06、现有基线 `electron/src/main/sources/gpu.ts` 与 `electron/src/main/wmi.ts`。API 依据：[ManagementObjectSearcher.Get(ManagementOperationObserver)](https://learn.microsoft.com/en-us/dotnet/api/system.management.managementobjectsearcher.get)、[ManagementOperationObserver](https://learn.microsoft.com/en-us/dotnet/api/system.management.managementoperationobserver)、[System.Management 10.0.12](https://www.nuget.org/packages/System.Management/10.0.12)。
