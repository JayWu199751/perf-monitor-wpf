# WPF 实现建议与验证边界

这是建议方案，尚无 WPF 实测结果；功能真值在 `spec.md`。最终取舍应在新仓库 ADR 中记录。以下 Microsoft 官方资料于 2026-10-03 读取；SDK/库版本在实际开工时重新核验，不硬编码「最新版」。

## 先验证最贵的不确定性

在正式 UI/业务扩展前，用可丢弃的 WPF 原生窗口探针完成：透明背景、可拖动命中区域、混合 DPI 换屏、顶/底任务栏行内可见性、点击任务栏后的 z-order 恢复。截图与桌面肉眼检查都要有。这是一个可运行的技术决策切片，见 ticket 02。

WPF 透明客户区需 `AllowsTransparency=true` 且 `WindowStyle=None`；仅把背景刷成透明不够。[Window.AllowsTransparency 官方说明](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency?view=windowsdesktop-10.0)。这支持透明窗口配置，但**不能由此推断磨砂能力、DWM 性能或任务栏可见性**。浏览器 `backdrop-filter` 不会自动变成 WPF 能力。

候选优先级：先保持真正 alpha 与正常命中，再比较原生背板效果的可用性/开销。若标准透明 WPF 窗口盖不住任务栏，运行小探针验证原生组合方案并记录 ADR；不要用一个不透明大矩形悄悄取消透明显示。不得直接移植 Electron 的 `pop-up-menu` 字符串档位。

## 建议模块

**低内存是硬性选择条件（F13）。** 同功能候选优先实测更低Private Bytes，常驻依赖要能说明用途。组合根优先直接构造必要服务；无必要不引入通用Host/DI容器、庞大UI/日志/监控框架。MVVM辅助库若使用，评估实际加载与常驻开销而不是只看包大小。缓存有上限、订阅与native句柄可释放，测量与优化流程见 `memory-budget.md`。

先用一个 solution、三个生产项目、两个测试项目；更少项目也可，只要规则可脱离桌面测试。不要按每个类/服务拆 DLL。

| 项目/模块 | 责任 | 测试合同 |
| --- | --- | --- |
| Core | Settings、快照、差分算法、贴边/行内几何、可见性 controller、设置窗生命周期、尺寸峰值策略、采样编排 | 不引用 WPF/user32，可用假时钟与端口跑行为测试 |
| Windows | CPU/内存/WMI/iphlpapi、前台窗口、显示器、计划任务、实例通知、token、托盘资源与 native 句柄 | Windows integration，布局/释放/异常/真实 OS |
| App | WPF XAML、ViewModel、应用生命周期、组合根、HwndSource 消息接线与 Dispatcher | UI 操作与真实桌面验收 |
| Core.Tests | 控制器/算术/状态机最高层行为 | `dotnet test`，数据驱动、确定时钟 |
| Windows.Tests | OS adapter 集成 | 标注 Windows/权限/硬件条件，单独运行 |

建议小窗 ViewModel 只暴露格式化段、有效主题和命令。WPF code-behind 接收 mouse/native 消息、报告实测尺寸，再交给 controller；不在那里维护第二套 dock、显隐或设置状态机。

## 少而深的接口

窗口 controller 只依赖「取真矩形、取显示器/工作区、落位、显示/隐藏、重申 z 序、监听变动」。设置服务依赖「持久化、广播、系统效果」。采样调度依赖快/慢源、单调时钟、UI 快照投递。Fake 捕获这些外部可观察结果即可。

初始 seam 不必为每次 P/Invoke 建接口；差分算法可以纯函数。native adapter 负责单位换算、消息映射和资源释放，规则留在 controller。组合根负责注入真实实现。

WPF `HwndSource` 等互操作设施可连接原生窗口与托管内容；使用它来接消息而不是复制 Electron IPC。[WPF/Win32 interop 官方说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-and-win32-interoperation)。

## 数据源建议

| 指标 | 建议 | 关键边界 |
| --- | --- | --- |
| CPU | P/Invoke `GetSystemTimes`，累计 kernel/user/idle 差分 | kernel 已包含 idle；利用率为 `(Δkernel+Δuser−Δidle)/(Δkernel+Δuser)`；首轮 0；失败缺失 |
| 内存 | `GlobalMemoryStatusEx` | 先设置 dwLength；用 TotalPhys/AvailPhys 原字节算比率，展示才取整 |
| 网速 | `GetIfTable2Ex` + `MIB_IF_ROW2` | 显式布局与 offsetof 对照、IfOperStatusUp=1、64 位计数、FreeMibTable finally、实际时间差 |
| GPU | `Process` 异步 nvidia-smi | 参数列表、UseShellExecute=false、CreateNoWindow=true、异步输出、5 秒超时及 kill/dispose，首条有效行 |
| CPU 温度 | 进程内 WMI/COM，或受支持的 System.Management | 后台查询、COM apartment 合同、超时/取消策略、热区单位/最大有效值、权限降级 |

`GetSystemTimes` 的 kernel 值含 idle；超过 64 processors 时它只覆盖调用线程所在主 processor group。若产品合同要求全机读数，应动态枚举活动组的有效 CPU mask，逐组以 `SetThreadGroupAffinity` 切换主组并读取、累加计数；PerfMonitor-WPF 的实现决策见 [ADR 0003](../docs/adr/0003-cpu-memory-sampling.md)。[GetSystemTimes 文档](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes)。

`GlobalMemoryStatusEx` 返回物理/虚拟内存信息，结果随系统状态变化，不能要求两次查询逐字节一致。[GlobalMemoryStatusEx 文档](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex)。

`GetIfTable2Ex` 的返回表由系统分配，使用完必须 `FreeMibTable`；成员间的对齐应依 SDK 定义验证，不能根据 Node 缓冲区随意猜偏移。[GetIfTable2Ex 文档](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getiftable2ex)。

旧 koffi 的外部 ArrayBuffer、str16 解码 AV 是旧运行时特定问题。C# 需要正确 StructLayout/packing、SafeHandle/finally、委托保活与线程边界，而不是照搬这些 JS workaround。EnumWindows/native hook 回调要持有强引用，生命周期内不得被 GC 回收。

## 坐标与文字

- 几何层只使用一种约定：建议桌面绝对位置/矩形以物理px，内容字号/尺寸为 DIP；类型名称显式标出单位。8 DIP/6 DIP/1 DIP 只在显示器 scale 边界换算一次。
- 开启并验证 per-monitor DPI awareness；native GetWindowRect/GetMonitorInfo 与 WPF Left/Top/ActualWidth 的单位不同，读取后立即适配。负坐标、多屏缩放时不能简单把屏幕全局 x/y 乘同一个 scale。
- 流程为布局 → 落请求尺寸 → 读取真实窗/卡矩形 → 求解 → 一次落位；重入门控，禁止靠无上限 settle 迭代磨掉设备网格误差。旧 Electron 的 38 DIP 下限与逐次 +1 DIP 故障是参考，WPF 需重新探测。
- 使用本机稳定字体（优先 Segoe UI Variable Text / Segoe UI，中文 Microsoft YaHei 回落），资源字典统一令牌；不依赖 SF Pro 存在，不把 Web 字体下载作为前置条件。
- 用 GlyphRun/FormattedText/实际绘制像素获得本引擎墨迹指标，单位角色共用一份表；主数字与时钟决定 cap 线和高度，`°` 独立参照。合成位图像素/逻辑视口大小交叉验证 scale，不信单个 metadata。
- WPF 的 TextBlock/Run BaselineAlignment 只是候选实现，不是已验收方案；以主体中线、上下边距、上标、裁切五类像素判据约束。
- 输出 10–18 字号 × 有值/缺失/100%与多位网速 × 100/125/150/175/200% DPI × 深/浅主题的证据。换字体栈必须重测。

## 生命周期、线程与竞态

- `Application.ShutdownMode` 建议显式 shutdown：托盘应用不能因唯一可见窗口关闭而意外退出。设置窗 Hide 不等于 Close，真正 Close 后必须重新创建实例。[WPF 窗口生命周期说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/)。
- 保留 settings controller 的 5 分钟回收行为，但不能复述 Electron 的「释放独立 renderer 37MB」为 WPF 效益。WPF 释放的是托管对象、native 资源和引用，收益重新测量。
- dispatcher 只做快照与视觉更新。后台 async 采样通道串行，CancellationToken/代际隔离跨 stop/restart；pause 和 move 独立状态，重启服务继承当前状态。
- 不让 await 同步堵住 UI；异常映射成对应源 null 与节制日志。WMI 无法立刻硬取消时也要保证迟到结果不会写回 UI；运行任务数量有界。
- taskbar 守卫是条件性机制：先真机证明需要，再按已有节奏实现。缓存与早退留在规则可测层，adapter 不藏轮询规则。

## 权限、自启、实例协调

建议写 ADR 选择 asInvoker + Release 一次 runas；拒绝保留当前实例继续。requireAdministrator 在创建进程之前就受 UAC 管控，和「原进程继续降级」有根本区别。Debug 永不自动提权。

单实例机制需跨同一用户的 elevated/non-elevated 实例；mutex 标识、ACL 与本地通知（named pipe 或受限本地 IPC）需要正反两种权限顺序测试。交接时先完成实例/权限握手再释放旧实例，尝试标记防循环；不能仅靠启动参数声称已经成功。

计划任务仅绑定当前用户登录、最高权限、交互式 GUI session。创建/删除后查询真实任务状态和目标路径，持久化真实结果。直接用 Task Scheduler API 或一次隐藏 schtasks 都可，属于低频配置副作用；稳态采样禁止 PowerShell/cmd/netstat。旧启动项清理只能清已确认属于该 app 的条目。

## 配置与性能基线

新 schemaVersion、范围校验、未知键策略、损坏备份、临时文件+原子替换，写入失败反馈；位置防抖与普通补丁由中央存储合并。明确导入旧 JSON 时保留支持字段，不能把冻结样例路径当本机真实 userData。

同机对照测试：冷启动、启动 30 秒后、5 分钟稳态、打开设置/关闭 5 分钟、隐藏、拖动、行内、休眠恢复。测整棵进程树 Private Bytes（包含 nvidia-smi 峰值和占空比），CPU 用时间差折成单核占比，记录操作系统/硬件/DPI/权限/刷新设置。历史 107.2MB 只作参考；没有新测量不宣称节省多少。

F13进一步要求至少3轮配对测量，稳态均值/P95低于当前旧版且峰值不更高，并完成1小时稳态/50轮设置/20轮显隐检查。WPF GPU加速、self-contained、单文件与裁剪分别比较，不照搬旧Electron关GPU的62MB收益。强制GC只能用于诊断可达性，不能成为周期性生产策略。

## 决策清单

已定：C#/WPF、Windows 11 x64、原生 XAML、功能等价、NVIDIA-only、物理网卡优先取最大、独立配置/任务、单份位置、无云功能。

建议待验证：SDK 与依赖、透明窗口/磨砂实现、任务栏 z-order、asInvoker 提权交接、CPU/内存具体 API、发布形式、明确导入入口。这些进入目标仓库 ADR；探针结论必须能复验。
