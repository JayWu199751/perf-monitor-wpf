# 纵向分票草案

这是准备材料，尚未经 /to-tickets 颗粒度/阻塞审批。正式发布时按目标repo tracker，每票一个 .scratch/wpf-rewrite/issues/NN-*.md，并用 canonical Status: ready-for-agent；不要把本目录直接当ready票执行。

每票打通可演示行为，规则、native、UI、存储按需一起完成。票02先验证透明/任务栏风险，避免最后才发现核心窗口方案不可用。阻塞关系允许数据源票在共同基线上独立推进；实际是否使用子agent由所选技能和用户授权决定。

| 编号 | 交付 | 阻塞于 |
| --- | --- | --- |
| 01 | [启动即有一个性能小窗与托盘](01-app-shell.md) | 无 |
| 02 | [证明透明小窗能在任务栏行内工作](02-native-window-probe.md) | 01 |
| 03 | [小窗显示真实CPU和内存](03-cpu-memory.md) | 01 |
| 04 | [小窗显示符合物理网卡口径的网速](04-network.md) | 03 |
| 05 | [小窗显示GPU与ACPI温度并正确降级](05-gpu-temperature.md) | 03 |
| 06 | [四组设置即时作用于性能条](06-settings-layout.md) | 03/04/05 |
| 07 | [拖动贴边并跨重启恢复真实位置](07-drag-dock.md) | 02/06 |
| 08 | [任务栏行内可见且可持久水平居中](08-taskbar-placement.md) | 07 |
| 09 | [全屏隐藏保留手动选择并暂停采样](09-fullscreen-visibility.md) | 07 |
| 10 | [两处共享菜单与透明显示一致](10-shared-menu-theme.md) | 08/09 |
| 11 | [设置窗可复用回收且退出不残留](11-settings-lifecycle.md) | 06/10 |
| 12 | [普通与提权启动只有一个实例并可降级](12-elevation-single-instance.md) | 11 |
| 13 | [正式版最高权限自启并独立迁移配置](13-autostart-migration.md) | 12 |
| 14 | [交付功能等价的Release与完整证据](14-release-acceptance.md) | 04/05/08/09/10/11/12/13 |

默认按编号在阻塞完成后逐票工作。可以开始的frontier票不一定要并行。最高层测试seam随正式spec核对，票内具体实现路径由最终架构决定。


