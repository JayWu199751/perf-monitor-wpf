# 验收矩阵 A01–A47（工票 15；A47 为 ADR-0007 增补）

状态：矩阵已建立（2026-10-05，工票 15）。每项逐条核实了自动化/集成证据与工票 Comments；**真实桌面/硬件人工验证均未执行**，相应项按规格要求标「未验证」并附复验步骤指针。本文不声称任何项已通过人工验收。

## 口径与约定

- 证据类别（沿 `rewrite-wpf/acceptance.md` 的层次定义）：
  - **A** = 自动化测试（程序集 `tests/PerfMonitor.Core.Tests`，经 Core 行为合同接缝）。
  - **W** = Windows 集成测试（程序集 `tests/PerfMonitor.Windows.Tests`，调用真实 Win32/WMI/schtasks 结构）与真实进程 smoke。
  - **M** = 真实桌面/硬件/权限人工验证（本轮未执行）。
- 状态定义：
  - **已证实**：该编号在 acceptance.md 中所列层次全部由 A/W 证据覆盖（含工票 Comments 记录的真实进程 smoke）。
  - **部分证实**：A/W 可覆盖的子项已有证据；该编号所列 M 层（真实桌面/硬件）子项未验证。
  - **未验证**：无足够 A/W 证据覆盖其核心场景，或场景本身全部依赖 M 层。
- 证据指针格式：工票文件（`.scratch/wpf-rewrite/issues/NN-*.md`，下文简写「工票 NN」）+ 测试类名；各工票 Comments 日期均为 2026-10-05。
- 人工复验步骤**复用各工票 Comments 中已写步骤，不在此重写**；无现成步骤的项在表内给出最小步骤。
- 基线：186 项自动化测试全过（Core.Tests 139 + Windows.Tests 47）、构建 0 警告 0 错误（docs/release.md「已验证」节，2026-10-05）。

## 矩阵

| 编号 | 需求 | 场景与可观察结果（摘录自 acceptance.md，保持原文语义） | 层次 | 状态 | 已证实证据（A/W） | 未验证部分与复验步骤 |
| --- | --- | --- | --- | --- | --- | --- |
| A01 | F01 | 冷启动只有 1 小窗+1 托盘；设置窗未创建，小窗无任务栏按钮/控制台 | W/M | 部分证实 | Core `StartupShellContractTests`「启动后只显示一个性能条和托盘，不抢焦点，也不创建设置窗」；工票 01：Debug WinExe smoke PE Subsystem=2（无控制台）、EnumWindows 仅一个可见「性能小窗」HWND；工票 02：UI Automation 在 Shell taskbar 下未找到「性能小窗」按钮（间接证据） | 托盘图标外观、任务栏按钮、真实冷启动全流程。复验：工票 01 Comments「待人工验收」段；docs/release.md「干净启动人工复验步骤」第 2–3 步 |
| A02 | F01 | 连续普通启动、普通→管理员、管理员→普通，均只有一个实例；已有小窗被手动唤起 | W/M | 部分证实 | Core `StartupShellContractTests`「普通或管理员重复启动都通知现有实例」「已提权的 runas 子进程只在有效交接时接管」「未提权的交接子进程退出并保留普通实例」「重复启动时即使性能条已隐藏也会手动显示并激活」；工票 03：Debug 同令牌双启动 smoke 314 ms 内退出码 0 | 真实 UAC 批准/拒绝与跨权限接管。复验：工票 03 Comments「未验证及复验步骤」段 |
| A03 | F01/F11 | 设置窗 X 仅隐藏；托盘退出/注销放行，无进程、timer、子进程、托盘残留 | A/W/M | 部分证实 | Core `StartupShellContractTests`「关闭设置窗只隐藏设置，不退出应用」「从基础菜单退出会清理整个应用壳」「退出时取消待执行的回收且到期不再释放」；工票 11：退出释放托盘图标与 HICON；工票 12：退出链幂等合同、`WindowsSlowMetricsSource` 取消即杀进程树 | 注销/关机真实会话终止、幽灵托盘图标与 nvidia-smi 残留观察。复验：工票 12 Comments「未自动验证项」第 2–4 步 |
| A04 | F02 | CPU 首轮 0；已知 idle/kernel/user 差分得到正确全机利用率；totalDelta<=0 不产生 NaN | A/W | 已证实 | Core `PerformanceMetricsContractTests`「首次 CPU 基线和内存百分比进入用户可见快照」「CPU 差分把 kernel 含 idle 的 75.5% 按 Math.round 语义取为 76%」「CPU 累计时间没有变化时显示零而不是 NaN」「活动处理器组变化时本轮 CPU 缺失，并以新组集合重建差分基线」；Windows `WindowsSystemMetricsSourceTests`「CPU 读数覆盖本机活动处理器组并恢复调用线程亲和性」。注记：当前主机单活动组，多组硬件聚合路径未实测（工票 04 Comments，非产品限制） | 无（多组主机限制已在证据中注明） |
| A05 | F02 | 内存原字节算比例，再四舍五入；不是先舍入 GiB；读失败显示 -- | A/W | 已证实 | Core `PerformanceMetricsContractTests`「内存使用率先按原始字节计算，GiB 只用于快照展示」「源读取缺失显示为缺失，CPU 恢复后先建立零基线」；Windows `WindowsSystemMetricsSourceTests`「Windows 系统源可读取物理内存原始字节」 | 无 |
| A06 | F03 | 有 NVIDIA 单/多行/空输出/N/A/不合法数字/总显存为0/超时/命令缺失，按合同显示或缺失 | A/W | 部分证实 | Core `PerformanceMetricsContractTests` 慢通道合并/失败隔离组；Windows `WindowsSlowMetricsSourceTests`「可查询本机 GPU 与 ACPI 数据或安全返回缺失」「调用前取消时不会启动系统查询」；工票 06：本机单卡实测（GPU 37%/45%/80°）、超时/取消杀进程树实现与 ADR 0004 | 多卡首条有效行、命令缺失、非法输出、总显存为 0、真实超时。复验：工票 06 Comments「复验步骤」段 |
| A07 | F03 | 无 NVIDIA/仅 AMD/Intel 不崩溃；其他指标正常；nvidia-smi 隐藏控制台、5 秒后取消释放 | A/W | 部分证实 | Core：GPU 读取失败时其余指标照常进入快照（`PerformanceMetricsContractTests`）；工票 06：无控制台异步子进程、超时/取消终止进程树（ADR 0004）；Windows `WindowsSlowMetricsSourceTests` 安全返回缺失 | 无 NVIDIA / 仅 AMD / Intel 机器的真实行为。复验：工票 06 Comments「复验步骤」段（移除 nvidia-smi、模拟超时） |
| A08 | F03 | 非管理员/无 ACPI/拒绝显示 --；管理员有热区时温度真值与 WMI 查询对照，声明是热区 | W/M | 未验证 | 仅缺失路径侧面证据：本机 ACPI 查询返回缺失（工票 06 Comments，原因未定）；Core 缺失语义合同 | ACPI 成功读数、多热区最大值、管理员真值对照。复验：工票 06 Comments「复验步骤」段（可访问 ACPI 热区的主机 + 非管理员条件） |
| A09 | F04 | 物理行15MB/s、TUN47MB/s选择物理；同物理filter6行不加总；上/下行选同一行 | A/W/M | 部分证实 | Core `StartupShellContractTests`「网络段优先取物理网卡，并让上下行来自总速率最高的同一接口」；Windows `WindowsSystemMetricsSourceTests`「可读取网卡计数和单调时间戳」 | 真实双向流量下用户可见的方向/单位/单行不累加。复验：工票 05 Comments「尚未在真实性能条窗口中…」段（可控双向流量 + TUN/Hyper-V 并存） |
| A10 | F04 | 无物理候选才回退全部up非loopback；down/loopback排除，枚举Up=1 | A/W | 已证实 | Core 网络合同：Up/非 loopback 基础筛选、Down 与 loopback 排除、无物理候选回退（工票 05 Comments 列举的合同覆盖；`StartupShellContractTests` 网络组） | 无 |
| A11 | F04 | 1.2秒真实间隔用1.2而非标称1；64位大计数无溢出；首轮/新增接口/倒退时间/计数复位为0 | A | 部分证实 | Core `StartupShellContractTests`「网络计数回退只清零该方向，非正采样间隔双向返回零且小数中点向上」「网络读取失败显示缺失，恢复后仍按单调时间差与保留基线计算」「完整接口表中消失的网卡重现时重新建立零速率基线」 | 64 位大计数无溢出无专项合同测试。最小复验：构造接近 2^64 的 ifTable 计数替身输入运行 Core 网络合同替身，或真机长时间运行观察无突降 |
| A12 | F04 | 候选变化保留全量基线；隐藏后恢复重新基线，不显示突发天文网速；睡眠/重连合理 | A/W/M | 部分证实 | Core：接口表代际清理与重现零基线（`StartupShellContractTests`「完整接口表中消失的网卡重现时重新建立零速率基线」）、隐藏重建基线（`FullscreenVisibilityContractTests`「暂停后的首次网络采样建立新基线，下一轮才算出速率」） | 真实睡眠/唤醒、网卡重连场景。复验：工票 05 复验步骤 + 系统睡眠唤醒后观察网速无天文数字 |
| A13 | F05 | 快1/2/5秒、慢3/5秒独立，慢源失败不阻塞快源，时钟仍独立秒级 | A/W | 部分证实 | Core `PerformanceMetricsContractTests` 快/慢通道周期接受与拒绝、双向失败隔离（「GPU 读取失败时…」「ACPI 温度读取失败时…」）；`StartupShellContractTests`「运行中更改快/慢刷新间隔会重排下一次采样」 | 「时钟仍独立秒级」无专项测试（见 A16）。复验：见 A16 |
| A14 | F05 | 同通道慢读取时任务数<=1；stop/restart迟到结果不回写；服务继承pause/move状态 | A | 已证实 | Core `PerformanceMetricsContractTests`「停止与重启期间不重叠慢通道读取，旧代际迟到结果不覆盖新快照」；`StartupShellContractTests`「性能条隐藏或原生拖动期间更改刷新率不会恢复采样」（不重叠采样与代际隔离） | 无 |
| A15 | F05 | hide暂停采样；show立即补采样；moving暂停、只缓冲最新，在move结束且可见时恢复 | A/M | 部分证实 | Core `FullscreenVisibilityContractTests`「自动隐藏期间暂停快慢采样，退出全屏恢复并立即补采样」；`PerformanceMetricsContractTests`「隐藏再显示会重建采样基线，旧代际在途读数不能覆盖新快照」「采样暂停恢复后，慢通道首轮发布保留快通道旧读数」（ADR-0006：恢复时不清空用户可见快照，不闪现缺失占位） | 「moving 暂停、只缓冲最新、move 结束且可见时恢复」无专项合同测试。最小复验：真实桌面按住标题区拖动数秒，观察松手后读数保持原值直到新采样到达（不闪现 `--`）且拖动期间无积压跳变 |
| A16 | F05 | 关闭时钟取消专属timer；开启立即显示本地24小时HH:mm:ss，不依赖metrics | A/W | 未验证 | 无专项测试证据（时钟为 `PerformanceBarViewModel` 内独立 DispatcherTimer，工票 07 Comments 仅实现说明） | 最小复验：设置中关闭时间指标 → 性能条时间段立即消失；重新开启 → 立即显示本地 24 小时 HH:mm:ss；断网/慢源故障时时钟仍每秒走 |
| A17 | F06 | 五个开关各自生效，组合与顺序正确，分隔线只在相邻可见段之间 | A/M | 未验证 | 无专项自动化合同（菜单分隔线对象 `ShellMenuItem(ShellMenuAction.Separator)` 有结构断言，但性能条可见段分隔线属 VM/XAML 层，工票 07 将其列入人工复验） | 复验：工票 07 Comments「待人工复验」第 4 步（隐藏任意指标组合检查顺序与分隔线） |
| A18 | F06 | CPU99→100→99，窗口宽跟随内容可增可减（ADR-0006）；个位数等数字宽占位符防抖；高度双向 | A/M | 未验证 | 无专项自动化合同 | 复验：真实桌面观察宽度随位数增减边界变化（如 9.9↔10.0、99↔100），个位数读数不引起宽度抖动，行内居中开启时宽度变化后回中 |
| A19 | F06 | 全部关闭仍有64DIP命中区域，从托盘可恢复；無裁切、无0宽窗 | A/M | 未验证 | 无专项自动化合同（工票 07 将「全关时仍保留至少 64 DIP 命中区」列入人工复验） | 复验：工票 07 Comments「待人工复验」第 4 步 + 托盘左键恢复 |
| A20 | F07 | 四边8DIP内悬靠，四角组合规范掩码；透明外边距推出，可视卡片保留安全边框 | A/M | 部分证实 | Core `PlacementRulesTests` 14 条（四边/四角/安全边框/DPI 缩放/sticky/透明 insets）；`PerformanceBarPlacementContractTests`「启动后按待恢复位置与贴边掩码恢复窗口位置」 | 真实桌面拖动贴边观感。复验：工票 08 Comments「未能自动验证」第 1 步 |
| A21 | F07 | 向内拖离>8DIP解除；移动原生跟手、右键不误拖动，松手只结算一次 | A/M | 部分证实 | Core `PlacementRulesTests`（阈值解除、sticky 语义）；`StartupShellContractTests`「性能条左键请求原生整窗移动，右键只打开菜单」；工票 02：真实 WPF smoke 从透明角落注入左键拖动窗口随指针移动、右键弹菜单主窗矩形不变 | 真实鼠标手感与「只结算一次」。复验：工票 08 复验第 1 步 + 工票 01 托盘/菜单人工验收 |
| A22 | F07 | 设置修改与500ms位置防抖交错保存最新真值；退出前flush；创建/恢复/同步moved不覆盖 | A | 已证实 | Core `PerformanceBarPlacementContractTests`「创建期临时位置不会覆盖待恢复位置」「原生拖动结束结算贴边并按防抖持久化最后位置」「防抖期间更改其他设置会合并最新位置且不再单独保存位置」「退出时立即写入防抖中的最后位置」「重复相同位置结算不重摆窗口也不重复保存」 | 无 |
| A23 | F07 | 副屏负坐标、混合DPI换屏、拔屏、改变分辨率/工作区后小窗可见且贴边正确 | A/W/M | 部分证实 | Core `PlacementRulesTests`（负坐标中心解析/最近显示器/夹回/恢复重放）；`PerformanceBarPlacementContractTests`「工作区或分辨率变化后按新工作区重新落位」「目标显示器消失时夹回剩余显示器工作区并持久化」；Windows `WindowsDisplayEnvironmentSourceTests` 3 条真实枚举 | 真实多屏、混合 DPI、插拔。复验：工票 08 Comments「未能自动验证」第 2–3、5 步；混合 DPI 步骤详见工票 02「混合 DPI 复验步骤」 |
| A24 | F08 | 顶/底水平任务栏行内，卡片中心决定落点、纵向居中；左右任务栏/无行回普通落点 | A/M | 部分证实 | Core `TaskbarRowRulesTests` 12 条（顶/底行派生、左右无行、行内垂直居中、行外回落）；`PerformanceBarPlacementContractTests`「拖入底部任务栏行松手后垂直居中到行且横向跟随拖动」「拖出任务栏行后回落普通贴边」 | 真实任务栏拖入。复验：工票 09 Comments「未能自动验证」第 1 步 |
| A25 | F08 | 行内居中默认关；开立即归中，拖动松手弹回；关原地不动，行外不影响 | A/M | 部分证实 | Core `PerformanceBarPlacementContractTests`「开启行内居中立即结算一次并弹回整行水平中心」「关闭行内居中原地不动保持松手位置」「行外开启行内居中不生效不重摆」；`TaskbarRowRulesTests`「行内居中开启时松手弹回整行水平中心」 | 真实菜单点击与持久化观察。复验：工票 09 复验第 2 步 |
| A26 | F08 | 连续20次点任务栏应用图标切换，不能永久被盖；测根句柄/z序，报告恢复延迟分布 | W/M | 部分证实 | Windows `WindowsTaskbarVisibilityGuardTests`（枚举任务栏根窗口、`WindowFromPoint`+`GA_ROOT` 遮挡判据实现、`SetWindowPos(HWND_TOPMOST)` 恢复）；Core 守卫节奏合同 | 真实 20 次点击切换与恢复延迟分布。复验：工票 09 复验第 3 步，并记录恢复延迟分布 |
| A27 | F08 | 若采用守卫：30ms快拍/300ms慢刷新、悬浮态不查遮挡、隐藏停止、explorer重启更新句柄 | A/M | 部分证实 | Core `PerformanceBarPlacementContractTests`「卡片驻留任务栏行内时运行可见性守卫并按慢周期刷新句柄」「悬浮态不运行任务栏遮挡守卫」「隐藏性能条后守卫停止」（节奏 30/300ms 可注入验证）；Windows `WindowsTaskbarVisibilityGuardTests`「重复慢刷新重新枚举句柄，explorer 重启后能恢复」（恢复路径） | 真实 explorer 重启观察。复验：工票 09 复验第 4 步 |
| A28 | F09 | F11/无边框全屏隐藏，退出恢复；普通最大化/点桌面/任务栏/本app不误判 | A/W/M | 部分证实 | Core `FullscreenVisibilityContractTests`（可见→全屏隐藏、退出恢复、不抢焦点）；Windows `WindowsFullscreenWatcherTests` 8 条（系统类排除、WS_CAPTION 最大化不算、2px 容差、cloak 排除、本进程排除） | 真实 F11 与全屏应用。复验：工票 10 Comments「未自动验证项」第 1 步 |
| A29 | F09 | 手动隐藏→全屏进出保持隐藏；自动隐藏→手动显示→退出不重复聚焦；下一周期重新自动 | A/M | 部分证实 | Core `FullscreenVisibilityContractTests`「手动隐藏的性能条在全屏进出时保持隐藏」「自动隐藏期间手动显示清两种隐藏标记且同一全屏周期内保持显示」「自动隐藏期间重复启动唤起走手动显示入口并保持同周期显示」 | 真实全屏 + 托盘交互组合。复验：工票 10 复验第 1、3 步 |
| A30 | F09 | 关闭自动隐藏：自动隐藏恢复，手动隐藏不恢复；watcher不因采样暂停而停止 | A/M | 部分证实 | Core `FullscreenVisibilityContractTests`「关闭自动隐藏时仅恢复因自动原因隐藏的性能条」「关闭自动隐藏时手动隐藏状态仍保持隐藏」「关闭自动隐藏后进入全屏不再隐藏，重新开启恢复观察」 | 真实桌面下 watcher 持续运行观察。复验：工票 10 复验第 1 步（关闭开关后反复进出全屏） |
| A31 | F10 | 左键托盘切换一次，右键两入口内容/勾选同步，同一操作不重复弹菜单 | A/W/M | 部分证实 | Core `StartupShellContractTests`「托盘左键单击可切换性能条显隐」「托盘和性能条右键显示共享菜单」「透明显示切换即时生效、持久化，且两处菜单勾选保持一致」；Windows `ContextMenuPresentationGuardTests` 3 条（重复消息抑制） | 真实托盘鼠标交互与 200ms 快速重复右键。复验：工票 11 Comments「未自动验证项」第 1 步 |
| A32 | F10 | 透明显示只剩文字/箭头/时钟，背景/线/阴影全部消失，命中区可拖动与右键 | M | 未验证 | Core 合同仅覆盖刷子/边框状态与命中语义（`StartupShellContractTests` 透明组） | 真机合成视觉效果。复验：工票 11 复验第 5 步 |
| A33 | F10/F11 | 浅/深/system变化和DPI变化，托盘黑/白与尺寸正确；explorer重启后图标恢复 | W/M | 部分证实 | Windows `TrayIconCatalogTests` 3 条（明暗前缀、16×DPI 就近档位、主题翻转）；工票 11：NotifyIcon 内建 TaskbarCreated 重注册实现说明 | 真实主题切换、DPI 缩放档位观感、explorer 重启。复验：工票 11 复验第 2–4 步 |
| A34 | F11 | 默认值与范围表一致；逐键补默认、指标深合并、controller独占位置、菜单独占两开关 | A | 已证实 | Core `StartupShellContractTests`「指标设置从单一持久化真值加载并在表单更改时深合并保存」「用户调整的不透明度按 0.05 步进并保留合法边界」「非法刷新间隔不会改变或持久化中央设置」；Windows `WindowsSettingsStoreTests`「设置文件写入 schemaVersion 并在重启后保留指标和外观」；菜单专属开关仅经 `SettingsPatch`（工票 09/11 实现与合同） | 无 |
| A35 | F11 | 设置即时反馈与小窗同步；持久化失败不报已保存；损坏JSON备份恢复，原子写中断可恢复 | A/W | 部分证实 | Core「持久化失败时中央设置和性能条都保留上一个成功值」；自启失败不显示成功（`AutostartContractTests`「端口报告失败时不保存并抛出异常避免显示成功」）；Windows `WindowsSettingsStoreTests`「损坏或越界设置会备份原文件并恢复默认」、临时文件+替换原子写（工票 07 实现） | 「已保存」约两秒提示的 UI 观感、原子写中断点复现。复验：工票 07 复验第 2–3 步；中断复现为保存瞬间强杀进程后重启检查配置可读 |
| A36 | F11 | 背景20/72/100%文字opacity不变；字号10–18；透明显示退出恢复既有背景值 | A/M | 部分证实 | Core `StartupShellContractTests`「透明显示往返保留背景不透明度值供退出时恢复」「用户调整的不透明度按 0.05 步进并保留合法边界」 | 真实视觉观感（背景 20/72/100% 对照、字号 10–18 逐档）。复验：工票 07 复验第 2、5 步 |
| A37 | F11 | 设置窗关闭→4分59秒重开取消回收，闲置5分钟释放并可重建；退出取消回收定时器 | A/M | 部分证实 | Core `SettingsIdleRecycleTimerTests` 5 条 + `StartupShellContractTests` 回收组 9 条（默认 5 分钟、重开取消、退出取消、可见不释放、重复关闭不重复排程） | 真实 5 分钟闲置回收与内存回落观察。复验：工票 12 复验第 1 步 |
| A38 | F11 | 低高度屏可滚动访问行为组，宽裕工作区无不必要滚动；不采用固定880DIP真值 | A/M | 部分证实 | Core `SettingsWindowSizingRulesTests` 3 条（MaxHeight 跟随工作区、保底、非法回退） | 真实低高度屏滚动观感。复验：工票 12 复验第 5 步 |
| A39 | F12 | Debug无UAC；Release确认/拒绝UAC、已尝试标志、token验证、实例握手，无循环双开 | A/W/M | 部分证实 | Core `StartupShellContractTests`「Debug 普通启动直接运行且不尝试提权」「Release 普通启动最多请求一次提权」「真实 elevated 令牌直接运行，不再次请求提权」；工票 03：`TokenElevation` 读取与单次 runas adapter、IPC nonce 一次性；docs/release.md 静态验证（asInvoker manifest） | 真实 UAC 批准/拒绝、runas 启动失败保留普通实例。复验：工票 03 Comments「未验证及复验步骤」段 |
| A40 | F12 | 自启开/关成功与失败回写真实状态；路径含空格/中文；当前用户登录以高权限无黑窗启动 | A/W/M | 部分证实 | Core `AutostartContractTests` 5 条（端口意图、实际状态回写、失败不保存）；Windows `WindowsScheduledTaskAutostartTests`（XML LogonTrigger/HighestAvailable/命令路径直写、XML 特殊字符转义、独立任务名）；注：schtasks.exe 被开发沙箱拦截，集成用例未真实执行（工票 13/14 Comments） | 真实创建/删除计划任务、注销重登静默启动、普通权限失败回滚。复验：工票 13 Comments「未自动验证项」第 1–2 步；docs/release.md 复验第 7 步 |
| A41 | F11/F12 | 新旧配置/任务/实例标识隔离；明确导入备份旧JSON，不删不属于app的启动项 | A/W/M | 部分证实 | Windows `WindowsSettingsStoreTests`「默认配置目录使用独立的本地 PerfMonitorWpf 命名空间」；`WindowsScheduledTaskAutostartTests`「默认任务名使用独立于旧版的 PerfMonitorWpf 命名空间」；工票 13：adapter 只按注入任务名操作、无扫描逻辑（实现与合同） | 与真实旧版（Electron）并存实测。复验：工票 13 复验第 4 步 |
| A42 | F12 | 清洁目录发布产物能运行（声明框架依赖）；资源/图标齐全，卸载/关闭自启后无任务残留 | W/M | 部分证实 | docs/release.md「已验证」（2026-10-05）：两种发布方式成功、`publish/fdd` 文件齐全（exe/deps.json/runtimeconfig.json/Resources）、PE Subsystem=2、asInvoker+PerMonitorV2 manifest、版本信息 VerQueryValue 实读、图标五尺寸嵌入；Debug smoke 6 秒存活优雅退出 | 仅运行时干净机器启动、真实卸载/关闭自启后无残留。复验：docs/release.md「干净启动人工复验步骤」第 1–8 步 |
| A43 | F13 | 同机同权限/设置至少3轮配对采样：整进程树Private Bytes稳定态均值/P95低于现版，峰值不更高；噪声内补测 | M | 未验证 | 测量工具与方案已就绪：`tools/memory-probe.ps1`（整进程树 Private Bytes，MiB=÷1024²，每轮重建进程树）+ `docs/memory-benchmark.md`（配对测量方案）。**无任何实测数字** | 全部实测。复验：按 docs/memory-benchmark.md 执行 ≥3 轮配对并填表 |
| A44 | F13 | 1小时稳态、50轮设置开关、20轮显隐，回收/稳定后内存平台不持续抬升；有托管/native诊断证据 | W/M | 未验证 | 压力步骤与判停标准已写入 docs/memory-benchmark.md | 全部实测。复验：按 docs/memory-benchmark.md「压力验证」节执行 |
| A45 | F13 | 常驻依赖、窗口/订阅、字体Brush缓存、WMI/托盘/native句柄、在途任务与采样子进程都有生命周期/上限 | A/W/M | 部分证实 | A/W：回收定时器生命周期（`SettingsIdleRecycleTimerTests`）、退出清理整壳（`StartupShellContractTests` 退出组）、慢源取消/超时杀进程树（`WindowsSlowMetricsSourceTests` + 工票 06）、采样代际隔离与不重叠（`PerformanceMetricsContractTests`）、SystemEvents/显示源/托盘 HICON 释放（工票 11/12 Comments） | 字体/Brush 缓存上限、native 句柄真实计数、长时运行观察。复验：工票 12 复验第 3–4 步 + docs/memory-benchmark.md 压力验证 |
| A46 | F13 | 至少记录关键候选方案或优化前后Private Bytes；完整功能不缩水，CPU/拖动/启动体验同时报告 | M | 未验证 | 内存预算与口径见 `rewrite-wpf/memory-budget.md`、`docs/memory-benchmark.md`；不预填数字 | 全部实测与记录。复验：按 docs/memory-benchmark.md 记录每轮 Private Bytes/CPU/启动/拖动体验与功能矩阵 |
| A47 | F01 | 悬浮条与设置窗从 Alt+Tab 与任务视图隐匿（ADR-0007）；任务栏无按钮维持不变 | W/M | 部分证实 | 运行时窗口样式实测（`.scratch/win_style_probe.py`）：修复前悬浮条 exstyle=`0x00080008` 无 `WS_EX_TOOLWINDOW` 且被 WPF 隐藏所有者持有（Alt+Tab 泄漏，用户截图 2026-10-05）；修复后 `0x00080088` 含 `WS_EX_TOOLWINDOW`；设置窗走同一 `NativeWindowStyles` 入口 | 真实桌面按 Alt+Tab 与 Win+Tab 人工确认无「性能小窗」「性能小窗 · 设置」条目；设置窗失焦后经托盘「设置」找回。最小复验：启动应用按 Alt+Tab/Win+Tab 观察；打开设置窗后切到桌面再按 Alt+Tab |

## 汇总统计（2026-10-05）

| 状态 | 数量 | 编号 |
| --- | --- | --- |
| 已证实（A/W 全覆盖） | 7 | A04, A05, A10, A13, A14, A22, A34 |
| 部分证实（A/W 有证据，M 层未验证） | 31 | A01, A02, A03, A06, A07, A09, A11, A12, A15, A20, A21, A23, A24, A25, A26, A27, A28, A29, A30, A31, A33, A35, A36, A37, A38, A39, A40, A41, A42, A45, A47 |
| 未验证 | 9 | A08, A16, A17, A18, A19, A32, A43, A44, A46 |

- 186 项自动化测试（Core 139 + Windows 47）全过、0 警告（docs/release.md，2026-10-05）。
- 所有 M 层人工验证（含 A43/A44/A46 内存配对与压力验证）**均未执行**；在人工复验完成前，交付状态按规格 F13 为「内存约束未满足/未验证」，功能验收不宣称完成。
- 视觉矩阵与判据（共线/居中/边距/上标/裁切/行内中心/对比度）见 acceptance.md 对应章节，均属 M 层，全部未验证。
