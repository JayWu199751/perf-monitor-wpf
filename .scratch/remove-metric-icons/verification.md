# 移除指标图标：验证记录

日期：2026-10-09

## 实现结果

- 设置窗删除「显示图标」分组、五个开关、事件处理和控件同步；「显示指标」整段开关保留。
- 性能条删除五个图形元素及几何资源、样式、图标画刷、专属尺寸和间距绑定；文字标签、读数、单位、网络上下行箭头、时间继续显示。
- Core 删除图标偏好模型、校验和局部更新接口；不保留恢复图标的隐藏设置。
- 旧 JSON 的 `metricIcons` 作为未知属性忽略，无需提升 schema 或操作真实用户配置；下次正常保存时自动移除该字段。
- 沿用自然宽测量和最小命中区，未新增布局策略或采样行为。
- 保留开始本任务前已有的主占用百分比颜色修改；更新词汇表、UI 参数、HTML 参考和验收矩阵，另在旧功能记录中追加替代说明。

## 自动化结果

| 检查 | 结果 |
| --- | --- |
| `dotnet test tests/PerfMonitor.Core.Tests/PerfMonitor.Core.Tests.csproj --no-build --no-restore --verbosity minimal` | 158 项通过 |
| `dotnet test tests/PerfMonitor.Windows.Tests/PerfMonitor.Windows.Tests.csproj --no-build --no-restore --verbosity minimal` | 55 项通过，1 项因桌面前置条件失败 |
| `dotnet test tests/PerfMonitor.App.Tests/PerfMonitor.App.Tests.csproj --no-restore --verbosity minimal` | 18 项通过 |
| `dotnet build src/PerfMonitor.App/PerfMonitor.App.csproj -c Release --no-restore --verbosity minimal` | 0 警告、0 错误 |
| `git diff --check` | 通过 |
| 搜索运行时代码中的废弃模型、绑定和处理方法 | 无残留 |

合计 231 项通过、1 项桌面前置条件失败；不将全量测试表述为全部通过。

## 本次行为覆盖

- 旧配置缺少 `metricIcons`、全部为 true、全部为 false、字段为 null 均保留其余设置，不产生损坏备份，保存后不再输出该字段。
- 设置窗打开、隐藏后重开、销毁后重建均无图标开关；整段开关继续保存和恢复。
- 真实 WPF 窗口在屏幕之外验证深浅主题和 10、12、18 DIP 字号：无图形元素、段首无图标专属占位、文字与网络箭头保留，代表性读数正常绑定，文字宽度无裁切。
- 自然宽按实际内容测量；隐藏所有段后保留 64 DIP 卡片命中宽及两侧各 16 DIP 投影留白；恢复整段后宽度恢复。
- 透明显示下继续保留文字，且不会重新出现图标。

## 全量测试限制

首次全量运行及单独复跑 Windows 测试时，`TaskbarGuardTopmostRegressionTests.Ensure_above_taskbar_elevates_card_above_real_taskbar` 均在第 134 行失败：测试调用任务栏守卫前，要求 `WindowFromPoint` 的根窗口等于任务栏，但实际命中了 Zen 浏览器的 `MozillaWindowClass` 窗口。

已通过只读窗口元数据核实该根窗口属于 `zen` 进程。本次未修改该测试或任务栏守卫代码，未为通过测试而调整用户浏览器窗口；此项不构成本次图标删除失败的证据，也不视为任务栏守卫验证通过。

## 人工验收（2026-10-10 用户确认通过）

- [x] 在真实桌面检查深色、浅色和跟随系统主题的最终观感与文字密度。
- [x] 实际拖动、贴边及任务栏行内居中，确认宽度收缩后的落位与投影。
- [x] 在不受其他窗口遮挡的桌面条件下复验任务栏置顶回归场景。

实现验证阶段未安装或替换正在运行的应用；随后按用户要求生成的安装包见下节。屏幕外 WPF 自动化不能替代以上桌面验收。

## 2026-10-09：按用户要求生成手动安装包

- 已完成 Release / win-x64 依赖框架发布及 Inno Setup 6 编译。
- 安装包：C:\Users\10854\Code\PerfMonitor-WPF\installer\Output\PerfMonitorWpf-1.0.0-setup.exe
- 大小：2294259 字节；SHA-256：6980530C76D0269168BDDB5A7290C6F685BF35B52F81EA93B5D732E1DC0BC657
- 发布目录中 App、Core、Windows 三个程序集的 SHA-256 均与本次构建一致，安装器编译日志确认打入这些发布文件。
- 包含本次移除指标图标功能及此前的百分比颜色调整；依赖 .NET Desktop Runtime 10.0。
- 未执行安装，交由用户手动安装。


## 2026-10-10：用户人工验收确认

用户明确确认‘除了内存我已全部验证通过’，本功能非内存验收完成。以上旧测试限制和未执行说明记录的是代理当时验证范围，不覆盖本次用户结论；没有把用户确认改写成代理重测。详见 [验收记录](../wpf-rewrite/acceptance-2026-10-10.md)。
