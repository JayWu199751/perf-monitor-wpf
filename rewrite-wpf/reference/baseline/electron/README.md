# 性能小窗（Electron 实现）

Windows 11 桌面性能小窗的 Electron + Vue 3 实现：Node 主进程（`node:os` + nvidia-smi + koffi 直连 WMI / iphlpapi）+ Vue SFC 渲染层。行为规格见根目录 `CONTEXT.md` 与 `docs/adr/`，面向使用者的说明见根目录 `README.md`，这里只记模块地图。

## 结构

- `src/main/` — Node 主进程
  - `settings.ts` / `settingsHub.ts` / `store.ts` — hub 拥有设置更新的完整流程：设置窗补丁、共享菜单开关、启动纠正、不可变快照、落盘与广播。位置经 `stagePosition` 即时更新内存、由 controller 防抖调用 `flushPosition`；期间用户修改设置也会保存最新位置，旧快照不会覆盖新设置。策略与系统效果经注入协作，`tests/settingsHub.test.ts` 覆盖完整流程
  - `metrics.ts` / `sources/` — 采样服务与四个数据源；快慢双频各自缓存，任一次采样都输出全量字段。`sources/network.ts` 把"读计数"与"算速率"拆开：读表只在 Windows 跑得起来，差分是纯算术，所以速率与选网卡的口径（只算物理网卡，ADR-0004）在 `createNetRateTracker` / `selectNetRows` 里，任何平台都能测
  - `dock.ts` / `geometry.ts` — 贴边悬靠的**纯几何**：只吃矩形只吐矩形，不碰窗口 API，`tests/dock.test.ts` 覆盖。唯一入口 `dockedPosition(input: DockInput)` 吃一个具名记录（窗口矩形 + 所在显示器的两个矩形 + 掩码 + 卡片尺寸 + 行内居中），**任务栏行**由显示器现场派生（`taskbarRow` = bounds − workArea，行内判定 `cardInTaskbarRow`），「卡片尺寸 → 请求的窗口尺寸」由 `windowSizingFor` 给（**钳制后的真尺寸不预测**，由小窗 controller 落尺寸后回读，见 ADR-0005 修正三）
  - `widgetWindow.ts` — 小窗的 deep module：拥有恢复门控、原生拖动门控、落请求尺寸→回读真矩形→求解→落位、贴边结算、500ms 防抖与退出 flush，以及显隐、档位、守卫和共享菜单不变量。**不 import electron**：端口只提供真实矩形、显示器与摆窗能力，module 的 interface 即测试面；`tests/widgetWindow.test.ts` 通过真实 adapter 映射与模拟 OS 钳制验证完整序列
  - `zOrderGuard.ts` — z-order 守卫的策略：30ms 一拍、行内状态每 10 拍慢刷、"不在行内连遮挡判定都不问"三条实测结论都在这里，被 `tests/zOrderGuard.test.ts` 钉住；机制与否决方案见 ADR-0005
  - `fullscreen.ts` / `fullscreenWatcher.ts` / `foreground.ts` / `win32.ts` — 全屏检测与可见性轮询；`win32.ts` 负责前台窗口/进程令牌的 koffi 边界，`wmi.ts` 负责 CPU 温度的 COM/WMI 边界，`netif.ts` 负责网卡字节计数的 iphlpapi 边界
  - `autoHide.ts` / `moveState.ts` — 手动与自动隐藏的优先级状态机；用 `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` 包住原生拖动区间
  - `autostart.ts` / `elevate.ts` — 计划任务自启（`ONLOGON /RL HIGHEST` + 清理旧 Run 键）；UAC 提权重启，提权与否由注入的 probe 判定
  - `settingsWindow.ts` — 设置窗的 deep module：懒创建、关闭即隐藏、隐藏满 5 分钟销毁、退出放行。不 import electron，窗口经窄接口注入，因此整条策略可单测（与 `widgetWindow.ts` 对称）
  - `widgetWindowPorts.ts` — 端口层：把上面那个 controller 需要的能力接到真实 `BrowserWindow` / `screen` / win32 FFI 上。全应用只有这一层碰 electron 又同时服务窗口，里面**只允许映射、不允许规则**
  - `tray.ts` / `paths.ts` / `window/` — 托盘与共享菜单、资源路径解析、两个窗口的纯工厂（生命周期一律上移到各自的 controller）
- `src/preload/` — `contextBridge` 门面，渲染层只拿得到 `PerfApi` 的 5 个方法
- `src/renderer/` — Vue 3 渲染层，`widget.html` / `settings.html` 两个独立入口
  - `src/widget/` — `WidgetApp` 用 `computed` 推导可见段与分隔线，`Reading` 用定宽槽位稳住数字，卡片尺寸经 `ResizeObserver` 上报
  - `src/settings/` — 分组卡片 + 四个受控行组件；单一数据源仍是主进程，改动经 IPC 回流
  - `src/lib/` — 无框架依赖的模块（时间格式化、网速槽位、尺寸上报）。`cardSize.ts` 的唯一入口接收布局盒与当前设置：只有字号/指标开关改变才允许缩窄，其余变化保留宽度峰值；高度双向更新，相同结果不重复上报。`WidgetApp.vue` 只观测与转发，测试覆盖设置/布局交错
  - `src/styles/` — 主题令牌 + `optical.css`（卡片几何的两个源头 `--edge-gap`/`--edge-bleed`，以及小窗文字共线的唯一定律与 a/ρ 两张无量纲常数表）；明暗由主进程 `nativeTheme.themeSource` 驱动 `prefers-color-scheme`，渲染层不管主题状态
- `src/shared/` — `Settings`/`MetricsSnapshot` 形状与 `WIDGET_EDGE_GAP`（最大字样到可视边框的边距，现 6px），主进程与渲染层同源。卡高不在此列：它由渲染端裁到墨迹派生后上报，`WIDGET_CARD_HEIGHT` 只是首帧兜底初值
- `scripts/alignment/` — 文字居中的真像素量具（调试装置，进不了包），六个文件各司一件、没有第七个：`measure.cjs` 按真 DPI 渲染卡片、`capturePage` 抓合成位图、逐 run 找墨迹上下沿取中线，四条判据是「共线（按各 run 的**主体中线**，不是墨迹外框）+ 居中（并集与主读数两个参照）+ 最大字样边距（现 6px）+ 不越出内容盒」，另有一条 `DSM_FONTS` 字体栈轴；`collect.cjs` / `preload.cjs` 是它自己要的两个采集器与假 preload；`fit-rho.cjs` 从 artifact 反解 a/ρ 两张表；`diff-readout.cjs` 对任意两份 artifact 复算**读数**（判据只在 `measure.cjs` 一处，差分器不复制阈值）；`gate-check.cjs` 对照 3·SE 这条趋势判据本身。跑法与复验步骤见 ADR-0006
- `tests/` — vitest：贴边几何（含行矩形派生与卡片→窗口尺寸）、小窗 controller 的窗口不变量、z-order 守卫的节奏、设置策略、全屏判定、显隐优先级、设置窗生命周期、采样竞态、提权与自启的命令构造、FFI 边界；另有 `opticalCenter.test.ts` 钉住文字居中的形状（定律只在一处执行、老机制 translateY 不许回来、容器不许用 flex 排文字、a 表与 ρ 表条目一一对应且都被认领、卡高必须由墨迹派生且上报高度只能是量出来的）

## 关键设计约束

- **拖动信号**：用 `hookWindowMessage` 抓 `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` 精确包住整个拖动循环，不去轮询左键状态；贴边吸附只在松手后结算一次，拖动中绝不 `setBounds`。
- **坐标单位**：`getBounds`/`workArea`/`setBounds` 全程 DIP，没有物理像素↔DIP 往返换算。两条硬约束是"以窗口中心解析显示器"和"**落尺寸之后回读真矩形**"（原先的"预测 OS 最小高度钳制"已作废：预测实测差 1~4 DIP，误差一半直接变成行内偏心，见 ADR-0005 修正三），几何求解与断言都在 `dock.ts` + `tests/dock.test.ts`，像素判据在 `scripts/dock/probe-row-center.cjs`。
- **渲染后端**：`app.commandLine.appendSwitch('disable-gpu')` 关掉硬件加速。小窗是一行文字 + 圆角卡片，没有需要 GPU 合成的内容，换来整棵 Electron 进程树私有内存 176.8MB → 115.1MB（GPU 进程自身 77.7MB → 15.6MB）。窗口内容用 `PrintWindow` 逐像素比对过，开关硬件加速的结果一致（平均亮度 28.0 对 28.2，颜色数 165 对 183）。
- **文字垂直居中**：分两件事，别混。① **居中与最大字样边距由盒子给**：`.bar` 是 `height: auto` + `text-box: trim-both cap alphabetic`，内容盒裁到「字号等于 `--fs` 那批 run 的墨迹外框」，上下再各加 `--edge-gap`——于是卡片中心线就是最大字样的墨迹中心，那一格边距与字号/DPI/哪族字体都无关，卡高随字号呼吸（目标 6px 时实测 20.8→26.4）。② **各角色彼此共线**才用定律：整条 bar 是一个共享基线的 inline 流（`.bar/.seg/.val/.reading` 都不用 flex 排文字），抬量由 `optical.css` 的 `.optical` 一处执行。两条不可协商的推论：执行器只能是 `vertical-align`——同流之后每个 run 都是非替换 inline 盒，`transform` 对它无效，写成 `translateY` 会让量具读数与自然态逐位相同（静默失效）；`align-items: baseline` 配 `translateY` 也不等价，transform 会连着 flex item 自己的基线一起挪。形状由 `tests/opticalCenter.test.ts` 钉住，数值由 `npm run measure:alignment` 判，取舍与否证见 ADR-0006
  **未确认的是逐像素 alpha**：`PrintWindow` 会把 alpha 拍平成不透明位图，而屏幕取样又要求桌面没被全屏应用盖住（实测连开着硬件加速的那份安装版都取不到），所以透明通道只能靠肉眼在真实桌面上看一次。真出问题删掉那一行即可，其余改动不受影响。

## 内存实测

稳态（小窗显示、设置窗未开过）取整棵进程树的 `PrivatePageCount` 之和，工作集因为共享页会虚高，不作为真实占用判断。CPU 温度现在由主进程直接调用 COM/WMI，不再有温度专用的 `powershell.exe`：

| 组成 | 私有内存 |
|---|---|
| Electron 树（主进程 + GPU + 渲染 + utility） | 约 115-120 MB |
| CPU 温度 WMI helper | 0 MB（进程内 COM） |
| **合计** | **约 115-120 MB** |

实际值会随设置窗口是否打开、WebView2/GPU 状态和系统缓存波动。WMI Provider Host 属于 Windows 系统服务，不是本应用的常驻子进程；查询失败时仅返回 `null`，界面显示 `--`。

## 注意事项

- dev 模式跳过 UAC 提权，CPU 温度显示 `--` 是预期行为。
- Electron 二进制与 koffi 原生模块的获取方式见根目录 `AGENTS.md` 的「环境坑」。`wmi.ts` 通过已存在的 koffi 直接调用 Windows COM/WMI，不需要额外安装 PowerShell 组件。
- `npm run dist` 已跑通：NSIS 安装器产出正常，koffi 经 `asarUnpack` 解到 `resources/app.asar.unpacked/`（含 `@koromix/koffi-win32-x64/win32_x64/koffi.node`），`extraResources` 把 `../resources` 整个图标目录拷到 `resources/resources/`，exe 内嵌 manifest 由 stock 的 `asInvoker` 改写为 `requireAdministrator`。
- 安装版（提权）已实测运行：CPU 温度读到真值、稳定态私有内存见上表。托盘图标与计划任务自启这两条在打包环境下仍未逐项实测。
