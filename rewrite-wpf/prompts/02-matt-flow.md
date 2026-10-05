# Matt Pocock skills 阶段提示词

本包是准备材料，尚未执行正式规格/分票审批。在新仓库运行技能；保持 idea→spec→tickets 连贯上下文。逐票实现时用新会话，材料和票文件作为入口。

## 初始化一次

```text
/setup-matt-pocock-skills
这是独立的 C# + WPF Windows 11 x64 仓库。请先检查已存在的 agent/tracker/domain 配置，保留我的指令。建议使用本地 markdown tracker：.scratch/wpf-rewrite/spec.md 和 issues/NN-*.md，canonical labels 保持默认。历史证据在 rewrite-wpf/reference/baseline/。本 repo 的领域词汇希望继续采用 CONTEXT.md + docs/adr/；若本机技能默认 GLOSSARY.md，请统一成一种布局并修正消费者指针，避免两份词汇真值。请按技能先呈现具体配置草案，再让我审阅。全过程中文。
```

## 准备规格

```text
/ask-matt
阅读 rewrite-wpf/README.md、spec.md、architecture.md、acceptance.md 和 prompts/01-master.md。我要功能等价重写，先识别真正需要选择的问题。请推荐本轮流程；把已有事实当成输入，避免重问字号、默认值、网速口径。目标是进入正式规格和纵向分票。
```

需要产品取舍时：

```text
/grill-with-docs
以 rewrite-wpf/spec.md 为现有需求草案，只讨论未决项：仓库位置、SDK、权限策略、发布形式、旧配置导入。既有功能保持不变。技术上能通过查源码/官方资料/可运行探针回答的问题请自行调查；需要观察 WPF 真窗口的问题记录为短探针，不用对话猜。按本 repo 的领域布局记录术语与ADR。
```

任务栏/透明风险要可运行答案时，独立做 `/prototype`；它不是继续修改本 HTML 原型：

```text
/prototype
建立可丢弃的 WPF 原生窗口探针，回答：真alpha透明、有命中区域的拖动、多DPI换屏、顶/底任务栏行内覆盖和点任务栏后的恢复能否同时成立？保留探针及本机数据作为决策证据。可参考 rewrite-wpf/spec.md 的F07/F08和architecture.md，不能把浏览器演示当真机答案。
```

换目录/新会话移交探针时按 `/handoff` 写可携带材料；探针完成后回到原规格上下文。不要让原型决定是否砍掉已要求的功能。

## 正式规格与分票

```text
/to-spec
将 rewrite-wpf/spec.md、architecture.md、acceptance.md、memory-budget.md、已确认决定与原生探针证据合成正式WPF规格，发布到本repo已配置tracker。保留F01–F13及验收映射、低内存硬约束、单份位置限制和明确兼容性修正。先与我核对controller/scheduler/settings等最高层行为测试接缝，再按技能模板发布。C#/WPF实现与历史Electron约束分清。
```

```text
/to-tickets
基于正式spec，审阅 rewrite-wpf/tickets/ 的14张纵向切片草案。调整颗粒度和阻塞边，保证每票可演示/验证并在一个新上下文完成；列出交付行为和blocked-by让我核对后，再发布到 .scratch/wpf-rewrite/issues/，每票一个文件并标ready-for-agent。不要把这些未批准草案直接当作已审阅任务，不重新triage技能自己生成的票。
```

## 两种实现方式，选择一种

逐票（默认）：

```text
/implement
读取 .scratch/wpf-rewrite/issues/<本票>.md、正式spec及 rewrite-wpf/acceptance.md。确认阻塞票完成后，以用户行为驱动TDD完成本票，使用原型为视觉参考，跑必要自动化/原生验证，再按code-review对照Standards与Spec。结果写回票中：改动、测试命令、证据、未验证项。完成本票再交接下一票。
```

整图编排（用户明确选择后）：

```text
/implement-spec
按本repo正式spec和已批准票的阻塞图实施。每个ready frontier票独立TDD，使用一条integration branch，集成后按code-review做双轴评审；保持原型仅参考、Win32真机单独验证。需要人在桌面验证的票留明确证据入口，不能用单测代替。全过程中文。
```

## 收尾

```text
/code-review
以实际重写开始的基线commit/branch作为比较点，按本repo标准和正式spec分别评审。覆盖F01–F13、acceptance中的证据缺口、低内存同机配对与压力验证、提权/自启/实例竞态、数据口径、资源释放与多DPI。报告真实问题及严重级别；没有发现也如实说。
```

```text
/retro
在本次实现上下文尚在时复盘，关注能改进下一轮执行的导航指针、像素量具、原生反馈环、标准与任务拆分。将机械错误优先变成确定检查。先给建议，不把未确认结论当作用户长期偏好。
```
