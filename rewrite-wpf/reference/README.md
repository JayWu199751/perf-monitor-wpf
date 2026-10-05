# 冻结旧实现参考

来源为2026-10-03的实际工作区，包含当时未提交变更。`manifest.json` 记录旧HEAD、工作区状态与每个复制文件的SHA-256；这不是声称旧工作区测试已经重新通过。

此目录只作旧行为证据，新WPF工程不要引用这里的Node/Electron依赖，也不要把历史AGENTS.md复制到目标根目录。版本由目标repo自己的标准决定。

| 关注点 | 先读 |
| --- | --- |
| 领域行为 | `baseline/CONTEXT.md`、`baseline/docs/adr/`（后续修正优先） |
| 默认设置、受控写入 | `baseline/electron/src/shared/types.ts`、`src/main/settings.ts`、`settingsHub.ts`及对应测试 |
| 几何、移动/恢复、防抖 | `src/main/dock.ts`、`geometry.ts`、`widgetWindow.ts`、`widgetWindowPorts.ts`及测试 |
| 显隐、全屏 | `src/main/autoHide.ts`、`fullscreen.ts`、`fullscreenWatcher.ts`及测试 |
| 采样与竞态 | `src/main/metrics.ts`、`sources/`及`tests/metrics.test.ts` |
| 网卡边界 | `src/main/netif.ts`、`sources/network.ts`及network/netif相关测试 |
| 温度边界 | `src/main/wmi.ts`、`sources/cpuTemp.ts`及WMI测试 |
| 托盘、菜单、自启、提权 | `src/main/tray.ts`、`index.ts`、`autostart.ts`、`elevate.ts`及测试 |
| 文字、尺寸峰值、格式 | `src/renderer/src/styles/optical.css`、`widget/`、`lib/cardSize.ts`、`lib/netSlot.ts`及测试 |
| 光学量具 | `baseline/electron/scripts/alignment/`，只借鉴像素判据，不能复制WPF校正常数 |
| 图标 | `baseline/resources/`，黑色应用三柱与深/浅托盘多DPI尺寸 |

表中的短 `src/` 与 `tests/` 路径均相对 `baseline/electron/`。只复制源码/规格/测试/图标，排除构建产物、node_modules、暂存诊断、生成的量具JSON/PNG。旧包包含的脚本若要实际运行，仍需完整旧工程与构建环境；这是参考快照，不是可独立启动的Electron项目。

## 已澄清的历史歧义

- ADR-0003原始固定26px卡片尺寸被ADR-0006动态墨迹卡高替代。
- ADR-0006修正六定宽槽被修正七自然内容宽替代；小窗文字共线按主体中线，°按cap线。
- 最新shared/types有透明显示与行内居中，设置表单不提供这两项；共享菜单是唯一入口。
- ADR-0005旧widgetDock实现名称被最新widgetWindow controller收敛替代，工作区内文件已删除。
- README「每屏各自记忆」未得到当前单份Settings.widget结构支持，详见本包spec。
- README「四条判据」落后于量具当前五判据；且ADR-0006末尾记录125%DPI边距尚红，不能只引用前面的全绿结论。
- 旧release manifest与运行时拒绝降级说明有创建进程先后冲突；目标权限方案必须独立设计并验证。
