# 事项跟踪：本地 Markdown

本仓库的事项和规格均以 Markdown 文件保存在 `.scratch/` 中。

## 约定

- 每项功能使用一个目录：`.scratch/<feature-slug>/`
- 规格文件为 `.scratch/<feature-slug>/spec.md`
- 每条实施事项单独保存为 `.scratch/<feature-slug>/issues/<NN>-<slug>.md`，从 `01` 开始编号，不把多条事项合并到一个文件。
- 每条事项文件顶部附近用 `Status:` 记录分流状态；状态角色见 `triage-labels.md`。
- 评论和讨论历史追加在文件末尾的 `## Comments` 标题下。

## 技能如何操作事项跟踪

- 技能要求“发布到事项跟踪器”时，在 `.scratch/<feature-slug>/` 下新建文件；目录不存在时一并创建。
- 技能要求“获取相关事项”时，读取用户提供的文件路径或事项编号对应的文件。

## Wayfinder 操作

Wayfinder 使用一份索引文件和每条事项各自的子文件：

- **索引**：`.scratch/<effort>/map.md`，正文包含 Notes、Decisions-so-far 和 Fog。
- **子事项**：`.scratch/<effort>/issues/NN-<slug>.md`，从 `01` 开始编号，问题写在正文中。用 `Type:` 标记事项类型（`research`、`prototype`、`grilling` 或 `task`），用 `Status:` 标记 `claimed` 或 `resolved`。
- **阻塞关系**：在文件顶部附近用 `Blocked by: NN, NN` 列出依赖项。列出的事项全部为 `resolved` 后，此事项才算解除阻塞。
- **待办范围**：扫描 `issues/`，找出未关闭、未阻塞且未认领的事项；优先处理编号最小的一条。
- **认领**：开始工作前，先将 `Status:` 改为 `claimed` 并保存。
- **解决**：在 `## Answer` 下追加答案，将 `Status:` 改为 `resolved`，然后把结论摘要和链接追加到 `map.md` 的 Decisions-so-far。