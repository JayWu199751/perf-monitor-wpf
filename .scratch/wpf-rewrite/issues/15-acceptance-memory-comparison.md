# 15: 完整验收与内存对比

**What to build:** 维护者能依据可复验的行为、桌面和资源证据判断 WPF 重写是否达到功能等价及内存目标。

**Blocked by:** 14: win-x64 Release 发布产物。

**Status:** ready-for-human

- [x] 验收矩阵 A01–A46 每项都有自动化、Windows 集成或真实桌面/硬件结果；不能执行的项标为“未验证”并附复验步骤。（矩阵已建立于 `docs/acceptance-matrix.md`：逐项核实证据并标注；M 层人工项统一标「未验证」并附复验步骤指针）
- [ ] 同机、同权限、同刷新率与指标设置下，旧版和 WPF 各测至少三轮整进程树 Private Bytes；记录稳定态均值、P95 和峰值。（方案成文于 `docs/memory-benchmark.md`，实测留待人工执行）
- [ ] WPF 稳态均值与 P95 低于旧版相应配对统计，峰值不高于旧版；接近测量噪声时补测，不以不确定结果宣称达标。
- [ ] 完成 1 小时稳态、50 轮设置开关、20 轮隐藏/显示压力检查；回收后内存平台不持续抬升。
- [ ] 每项优化和测量记录方案、Private Bytes、CPU、启动/拖动体验及功能矩阵；明显功能或交互退化不能作为内存达标的代价。（记录模板与规则见 `docs/memory-benchmark.md`，实测未执行）

## Comments

### 2026-10-05（实现者 impl-15-acceptance）

**产出**

- `docs/acceptance-matrix.md`：A01–A46 验收矩阵。逐项核对工票 01–14 Comments 与 `tests/` 全部测试 DisplayName 后标注证据类别（A=Core.Tests、W=Windows.Tests/真实进程 smoke、M=人工）与证据指针；汇总统计：已证实 7（A04、A05、A10、A13、A14、A22、A34）、部分证实 30（A/W 有证据、M 层未验证）、未验证 9（A08、A16、A17、A18、A19、A32、A43、A44、A46）。所有 M 层人工项附复验步骤指针（复用工票 Comments 已写步骤）。视觉矩阵判据（共线/居中/边距等）属 M 层，全部未验证。
- `tools/memory-probe.ps1`：整进程树 Private Bytes 探针（PS 5.1 兼容）。每轮经 `Win32_Process.ParentProcessId` 递归重建进程树（含 GPU 采样子进程），逐进程 `PrivateMemorySize64` 求和，MiB=÷1024²；输出 CSV（时间戳/树内 PID/进程数/总字节/MiB）与摘要（稳定态均值、P95 最近邻秩法、峰值，注明方法）；支持 `-WarmupRounds` 剔除预热样本。
- `docs/memory-benchmark.md`：新旧版配对测量方案。运行体入口（WPF 走 docs/release.md 产物、旧版走冻结源码既定入口）、同机同权限同设置对齐步骤、≥3 轮配对（冷启动→静置 5 分钟→采样 5 分钟@1s→退出，新-旧交替）、均值/P95/峰值比较规则与噪声补测规则（噪声阈值 = max(2×轮内标准差, 均值 2%)，接近则补测至多 2 轮，仍接近记「未验证」）、1 小时稳态（10s 间隔 360 样本，分段均值判抬升）、50 轮设置开关与 20 轮隐藏/显示（前后基线 + 静置 10 分钟判停）、诊断证据要求与结果记录表。**不含任何编造测量数字**，全部结论标「未验证」。

**探针冒烟证据（2026-10-05，本机）**

对自身 powershell 进程树采样 10 秒（间隔 500ms、20 轮、剔除前 5 轮预热），探针可用：

```text
目标根进程: PID 20572 (pwsh)
采样: 间隔 500 ms x 20 轮（预热 5 轮）
全部样本: 样本 20, 均值 54.894 MiB, P95 63.109 MiB (最近邻秩 rank=19), 峰值 64.113 MiB, 最小 42.621 MiB
稳定态(剔除前 5 预热轮): 样本 15, 均值 57.407 MiB, P95 64.113 MiB (最近邻秩 rank=15), 峰值 64.113 MiB, 最小 52.031 MiB
```

此冒烟仅证明探针可用，**不是**新旧版对比结果。

**构建与测试回归**

- `dotnet build PerfMonitor.sln`：0 警告 0 错误。
- `dotnet test PerfMonitor.sln`：Core.Tests 139 + Windows.Tests 47 = 186 全过、0 失败，与基线一致。沙箱拦截 schtasks.exe 导致自启集成用例按既有前置守卫跳过（工票 13 已记录的已知环境限制），不影响测试结论。
- 合入集成 tip：`git merge codex/wpf-rewrite-integration` = Already up to date（分支已基于 67ee531）。

**人工执行清单**

1. A43：按 `docs/memory-benchmark.md` 第 4 节执行 ≥3 轮新旧配对测量并填表。
2. A44：按第 5 节执行 1 小时稳态、50 轮设置开关、20 轮隐藏/显示压力验证。
3. A45/A46：按第 6 节记录诊断证据、CPU/启动/拖动体验与功能矩阵。
4. 其余 M 层功能项：按 `docs/acceptance-matrix.md` 各行「未验证部分与复验步骤」列执行（步骤指向各工票 Comments）。

全部人工测量完成前，F13 交付状态为「内存约束未满足/未验证」，功能验收不宣称完成。
