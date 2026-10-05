# 性能小窗 · C# / WPF 重写交接包

整理日期：2026-10-03。基于当前工作区实际文件（包含未提交修改），不是仅基于 HEAD。目标是从零实现一个功能等价的 Windows 11 x64 原生应用。

## 从哪里开始

1. 双击 [ui/index.html](ui/index.html)，查看可交互的桌面小窗、设置窗和共享右键菜单。无需安装依赖或联网。
2. 新建一个独立的 WPF 仓库，把整个 `rewrite-wpf/` 文件夹复制进去，作为只读交接材料。建议在新仓库根目录保留本文件夹名。
3. 将 [prompts/01-master.md](prompts/01-master.md) 的全文发送给 agent。这是完整重写启动提示词，默认先形成可执行规格，不直接开始大规模编码。
4. 按 [prompts/02-matt-flow.md](prompts/02-matt-flow.md) 使用你本地安装的 Matt Pocock skills。每一阶段都有可复制的提示词。
5. 实现时用 [prompts/03-implement-ticket.md](prompts/03-implement-ticket.md)，交付时用 [prompts/04-review-and-release.md](prompts/04-review-and-release.md)。

## 文件索引

| 文件 | 用途 |
| --- | --- |
| `spec.md` | 完整功能、默认值、状态转移、行为口径；本交接包的需求权威文件 |
| `architecture.md` | WPF 模块边界、Win32 数据源建议、DPI/提权/透明窗口等待验证项 |
| `acceptance.md` | 每项功能的验收场景、自动化与真机验证矩阵 |
| `memory-budget.md` | 新增低内存硬约束、优化顺序、整进程树A/B与泄漏验证 |
| `prompts/` | 启动、技能流程、单票实现、评审与交付提示词 |
| `tickets/` | 14 张纵向切片的**草案**，含阻塞关系；不是已经批准发布的任务 |
| `ui/index.html` | 单文件 HTML 原型，脚本和样式内嵌 |
| `ui/design.md` | 样式令牌、控件映射、交互说明、WPF 视觉验收要求 |
| `ui/validation.md` | 23项网页交互检查范围、结果与预览索引 |
| `ui/previews/` | 经浏览器渲染的原型截图 |
| `reference/baseline/` | 现有规格、ADR、源码、测试、量具和图标的冻结副本 |
| `reference/manifest.json` | 冻结文件的来源、SHA-256 与原仓库状态 |

## 范围与真实性

- 本次交付的是重写材料和设计原型；尚未实现或验证 WPF 应用。
- 追加要求：完整功能下尽可能降低内存。spec新增F13，提示词、分票与验收同步；需要同机实测证明低于现版，未验证不声称收益。
- HTML 中指标是示例数据；拖动、全屏、任务栏和托盘为浏览器内的模拟。它们不能证明 Windows 原生行为正确。
- 原型沿用现有单行条、四组设置与共享菜单；页面上的场景按钮、状态读数、导出配置属于原型工具，不属于产品新功能。
- `spec.md` 区分「必须保持的行为」「兼容性修正」「待验证的技术建议」。旧版 Electron 的最小窗高、z-order 档位和 CSS 光学系数不是 WPF 技术合同。
- 旧文档存在历史措辞：旧固定卡高、定宽数字槽、四条对齐判据等已经被后续修正替代；按当前源码及 ADR 最后修正理解。ADR-0006 明确记录 125% DPI 下仍有边距红项，不能把旧量具描述为全部 DPI 已通过。
- README 曾写「多显示器各自记忆」，但当前 `Settings.widget` 只有一份位置。重写默认保留**单份最后位置 + 多显示器正确落位与断开恢复**；逐屏位置档案是待用户确认的扩展。
- 新应用使用独立配置目录、实例锁和自启任务名；旧配置只在明确导入时迁移。不会自动覆盖正在用的 Electron 实例或计划任务。

## Matt 技能使用说明

`/ask-matt` 是路由器。此重写的建议路径为 `/grill-with-docs`（只处理真正未决的问题）→ `/to-spec` → `/to-tickets` → 每票 `/implement`（内部 `/tdd` 与 `/code-review`）→ `/retro`。一次编排全规格时可在分票获批后改用 `/implement-spec`。

流程提示词根据本机 `C:\Users\10854\.agents\skills\` 中实际技能内容编写。先在**新 WPF 仓库**配置技能的本地 tracker 和 domain docs；本包内 `reference/baseline/AGENTS.md` 是历史证据，不能直接放到新仓库根目录，因为它要求唯一实现在 Electron。

本包不是一次 `/to-spec` 或 `/to-tickets` 的已批准执行结果。那些技能要求的接缝讨论与分票审批，留给实际启动重写时完成；当前提供的具体草案可以直接拿去审阅。
