# AGENTS.md

本文件是 repo 内 agent 工作说明的唯一事实来源；`CLAUDE.md` 只指向这里，不另存副本。

## 项目

**性能小窗（Win11 Perf Monitor）**：Windows 11 桌面性能检测小工具，常驻托盘的透明置顶小窗，实时显示 CPU/内存/GPU/网速/温度等指标。自用工具，无分发 scope。

唯一实现在 `electron/`：Electron + Vue 3，Node 主进程（`node:os` + nvidia-smi + koffi 直连 WMI/iphlpapi）+ Vue SFC 渲染层。dev 运行、单测、`npm run dist` 打包、安装版提权运行都已验证。开发、构建、测试都要先 `cd` 进这个目录，命令看它的 `package.json` 的 scripts。

用户可见行为的规格就是 `CONTEXT.md` 的术语表加 `docs/adr/`：贴边悬靠、全屏自动隐藏、托盘显隐、手动/自动隐藏优先级、网速的物理网卡口径都以那里为准。`Settings`/`MetricsSnapshot` 的形状与 `WIDGET_EDGE_GAP`（最大字样到可视边框那格边距，现 6px）放在 `electron/src/shared/`，主进程与渲染层同源引用。**卡片高度不是常量**：它由渲染端把内容盒裁到最大字样的墨迹外框派生出来，再经 `resizeWidget` 上报，贴边推出与任务栏行内判定都吃上报值；`WIDGET_CARD_HEIGHT` 只剩首帧与未上报时的兜底初值，别拿它当几何真值。

## 环境坑

配置本身说不出来的那些，其余交给 `package.json`：

- `electron/` 的 Electron 二进制常常要手动补拉。`electron/package.json` 里的 `allowScripts` 是**按版本锁死**的白名单，条目与实际解析出的版本一旦对不上（`electron@44.2.0` 对 44.2.1 就不算数），npm 会静默跳过 postinstall，`node_modules/electron/dist/` 留空，`npm run dev` 报 `Electron failed to install correctly`。查有没有被漏掉的包：`npm approve-scripts --allow-scripts-pending`。
  补拉走 npmmirror（直连 GitHub 的 release 附件下载不稳，`git push` 倒是稳定）：`cd electron` 后执行
  `$env:ELECTRON_MIRROR='https://registry.npmmirror.com/-/binary/electron/'; node node_modules/electron/install.js`
- `electron/` 的小窗文字垂直居中是**两件事**，别混成一件：① 居中与「最大字样到可视边框那格边距」由盒子给——`.bar` 用 `text-box: trim-both cap alphabetic` 把内容盒裁到最大字号那批 run 的墨迹外框（cap 顶→基线）再加上下等大的 `--edge-gap`，所以卡高随字号呼吸、且没有任何可调数字；② 各角色彼此共线才用**那条定律**：`vertical-align: calc(-1 * (a * var(--fs) + ρ * 1em))`，执行处唯一在 `src/renderer/src/styles/optical.css` 的 `.optical`，a/ρ 两张表由 `scripts/alignment/fit-rho.cjs` 从真像素反解。**把卡高改回钉死一个数、或者给某个组件再补一个 translateY/字面抬量，都是把①退化成②**——前者实测让这一格随字号从 6.6 掉到 3.4，后者是当年的原始 bug。**改渲染层任何文字相关样式都必须跑 `cd electron && npm run build && npm run measure:alignment`**（19 组合、约 5 秒、退出码非 0 即红），它现在有五条判据：共线 / 居中 / 边距 / 上标 / 裁切，另有 `DSM_FONTS` 这条字体栈轴（换栈必红，不静默）。共线那条量的是**主体中线**（`CONTEXT.md`）而不是墨迹外框中线：外框会被 `MB/s` 的斜杠降部单方面拖走，按外框居中正好把单位顶到数字的 cap 线上——那是用户真实报过的症状，别把判据改回外框。上标那条（修正五）说 `°` 故意不吃共线：它对齐的是最大字样的墨迹上沿（cap 线），谁豁免只写在 `scripts/alignment/roles.cjs`，量具与反解器共用这一份名单。模型为什么长这样、各档实测数字、被否证的解释、以及「改哪样必须重测哪样」全在 ADR-0006，本文不留副本。
  两个只有踩过才知道的点：**执行器只能是 `vertical-align`**——定律写成 `translateY` 不报错，只会让 19 个组合的读数与自然态逐位相同（静默失效、量具全绿、屏幕没变）；**容器一旦改回 flex 排文字，共线极限就退回 1 设备px**。这两条由 `tests/opticalCenter.test.ts` 钉住，关定律重测的注入样式是 `.optical{vertical-align:baseline !important}`。
- `electron/` 里凡是量屏幕/窗口像素的，别信 `capturePage` 的 `nativeImage.getScaleFactor()`：HiDPI 下 175% 它报 1。缩放比必须从「位图总像素 ÷ 视口 CSS 尺寸」反推——`scripts/alignment/measure.cjs` 就是这么活的，任何新写的桌面量具都吃这条。
- `electron/` 的 vite 停在 7：electron-vite 5 的 peer 不含 vite 8。
- `electron/` 的 dev server 绑 `127.0.0.1`：Vite 只听 IPv4，而 Electron 解析 `localhost` 先试 `::1`，不写死就白屏。
- `electron/` 的 koffi 是原生模块，打包靠 `asarUnpack` 解到 `app.asar.unpacked`。已验证解包正确，含平台包 `@koromix/koffi-win32-x64/win32_x64/koffi.node`。
- `electron/` 里读任意 native 内存只能走 `koffi.decode` / `koffi.alloc`：`koffi.view()` 造的是外部 ArrayBuffer，Electron 的 V8 不允许外部缓冲区，一碰 COM vtable 就是 `FATAL ERROR: Error::New napi_get_last_error_info` 这种 native 崩溃，普通 Node 下却跑得好好的。所以 `src/main/wmi.ts` 的 COM 边界在 dev 之外没有第二道防线，改它务必 `npm run dev` 肉眼过一遍。
- `electron/` 的 dev 模式跳过 UAC 提权，CPU 温度显示 `--` 是预期行为，不是待修的 bug。安装版（提权）已实测读到 85°，进程内 WMI 链路是通的；非管理员下同一查询返回 0 行，所以 `--` 只能证明没权限，证明不了代码对——要验真值必须跑安装版。
- 量 `electron/` 内存要用私有内存合计，工作集被共享页抬到近三倍不作数；而且别只按进程名过滤——数据源曾经经 `systeminformation` 起 `powershell.exe`，那个进程名不是 `性能小窗.exe`，按名字统计会整个漏掉。现在 CPU 温度与网速都已改成进程内 FFI，`systeminformation` 依赖整个删掉了。
  安装版（提权）稳定态实测：主进程 43.4 + 小窗渲染 36.8 + gpu-process 15.6 + 备用渲染 11.4 = **107.2MB 私有**（工作集合计 309.1MB），25 秒内 app 的子进程里 `powershell`/`cmd`/`netstat` 零命中。两条数据源都改之前是 124.8MB（差的约 11MB 就是主进程里那份 systeminformation JS），且上面还要再挂一个常驻 107-141MB 的温度 powershell，外加网络每轮一个峰值 110.7MB、37% 占空比的短命 powershell——实际占用按 ~165MB 算，这轮净省约 58MB。
- `electron/` 的两条 native 边界各自成模块：`src/main/wmi.ts`（COM/WMI 取 ACPI 热区温度）、`src/main/netif.ts`（`iphlpapi!GetIfTable2Ex` 取网卡字节计数）。踩过的坑记在这里，别再试第二遍：
  `GetIfTable2` 在本机一调就 AV，只有多一个 level 参数的 `GetIfTable2Ex` 能用；`koffi.decode(addr, 'str16')` 解 `MIB_IF_ROW2.Alias` 也会 AV，所以 netif 只声明结构体让 `koffi.offsetof` 算偏移、逐字段读标量，字符串一概不解。
  另外 `MIB_IF_ROW2` 的 `OperStatus` 用的是 `ifdef.h` 里 `IfOperStatusUp = 1` 那个枚举，不是 `iprtrmib.h` 里 Up = 2 的旧枚举；同一块物理网卡的 filter 子接口共享同一份字节计数，选网卡必须取最大而不是求和。
  还有：候选集必须**先收窄到物理网卡**（`PhysicalMediumType` 非 `Unspecified`，即 `CONTEXT.md` 里的**物理网卡口径**）。本机 mihomo 的 TUN 网卡把同一份流量重复计入，15.4 MB/s 的下载记成 47.4 MB/s，而"取最大"每轮都正好挑中它。理由与备选判据见 ADR-0004。
- `electron/` 里凡是要把窗口摆到某个落点的，三条实测约束别踩（2026-09，量具 `scripts/dock/probe-row-center.cjs`，跑法 `npm run probe:dock`，`DSM_ATTACH=1` 可附着到正在跑的实例上量）：
  ① **别预测 OS 钳制后的窗口高**。`windowSizingFor` 只答"请求什么"；钳制在 OS 那侧，差 1~4 DIP（创建期窗口 41 DIP、按请求高落尺寸后 37 DIP），而它错多少、行内垂直居中就偏一半。落位顺序是「落尺寸 → `getBounds` 回读真矩形 → 求解 → 落位」。
  ② **落位一律重述"请求尺寸"**，别重述窗口现尺寸：`useContentSize` 的窗口，用 `setPosition` / `setSize(现尺寸)` / `setBounds(现尺寸)` 落位会让窗口高**每次 +1 DIP 往上爬**（连调三次 39→40→41，卡片跟着往下漂半格），重述请求值则三次都是 37 DIP。
  ③ **y 只能整 DIP**：`setPosition`/`setBounds` 收到小数直接抛 `conversion failure`。配合"OS 高度在 36~38 DIP 之间随落点奇偶抖 ±1 DIP"，行内居中的地板是 ±1 物理px，**不要写迭代 settle 来消它**——求解不收敛，多跑一轮只是来回跳。
  量"卡片在哪"必须用像素（capturePage 位图的 alpha 边界），**别用 DOM**：隐藏窗的 `innerHeight`/`getBoundingClientRect` 会滞后于真实窗口（实测 winH=38 时 innerH 仍报 40）。真机跑 Electron 时若 GPU 进程起不来（`GPU process isn't usable`），加 `--no-sandbox --disable-gpu-sandbox --in-process-gpu`；本仓 app 自己会 `disable-gpu`，所以必须补这三个开关才跑得起来。
- `electron/` 的小窗 z-order 档位钉在 `src/main/widgetWindow.ts` 的 `WIDGET_Z_LEVEL = 'pop-up-menu'`，**不能退回 Electron 默认的 `floating`**：Win11 的 DWM 把 layered（透明）窗合成在任务栏**之下**，`floating` 档下小窗拖进任务栏那一行会整块消失（表现为像素被任务栏的模糊面采样，不是报错）。原生不透明 GDI 窗没这个问题，Electron 连 `transparent:false` 也躲不过，因为它一律走合成表面。七档实测数据与被否决方案见 ADR-0005。
- 停在任务栏行内的小窗会被 explorer **反超**：点任务栏图标切应用那一刻，`Shell_TrayWnd` 被重新插到 topmost 带前面，小窗就永久压在任务栏半透明材质之下（体感"整块消失"，但窗口存在、`WS_EX_TOPMOST=True`、位置正确）。Alt+Tab 不触发。换更高档**不是解法**（同带，只是改概率）；解法是 `src/main/zOrderGuard.ts` 里 **30ms** 的 z-order 守卫（档位常量仍在 `widgetWindow.ts`，那个模块刻意不 import electron），判据是"卡片中心那一像素归不归 `Shell_TrayWnd`"。两条已实测的否定结论，别再去试：**换更高档无效**（`screen-saver` 与 `pop-up-menu` 都是 10/10 被反超），**也挂不上事件**（被反超时本窗口收不到任何 `WM_WINDOWPOSCHANGED`，只有自己重申时才收到），所以只能轮询——而**轮询间隔就是用户看到的闪烁时长**，200ms 时"点任务栏闪一下"就是守卫在捞它，压到 30ms 后恢复实测 13/20ms。
  开销别靠"就几个 user32 调用"外推：实测 30ms 定时器本身免费，贵的是每拍的 `FindWindowW` 类名字符串编组，缓存句柄 + "不在行内就早退"（注意 JS 会先求值全部实参，
  必须显式 `return`）后悬浮态与基线不可区分、行内约 0.5% 单核。**改 `zOrderGuard.ts` 的 `Z_GUARD_INTERVAL_MS` 做测量时务必当场还原**（这两个常数现在被 `tests/zOrderGuard.test.ts` 钉住，留成临时值会直接红）——留成临时值被 `npm run dist` 打进安装版，
  表现就是"点任务栏后小窗一直藏着"（60 秒才自愈一次）。机制与全部实测数字见 ADR-0005。**写这类桌面量具时必须把委托根住**：把 lambda 当场传给原生 `EnumWindows`，GC 能在枚举中途回收它，表现为"任务栏 `IsWindowVisible=True` 却枚举不到"，于是所有测量静默失真。**像素不能当判据**——卡片数字每秒自己在跳；合法判据是 z 序号或 `WindowFromPoint` 归属。另外别点开始按钮/托盘时钟做触发，会留下面板让 `Shell_TrayWnd` 不可枚举甚至成为前台窗口。
- `electron/` 关掉了 GPU 硬件加速（`app.commandLine.appendSwitch('disable-gpu')`，省 62MB）。它和透明无边框窗是相互纠缠的两件事：窗口内容能用 `PrintWindow` 比对，逐像素 alpha 不能（会被拍平成不透明位图），所以改这块时别拿 `PrintWindow` 的结论代替肉眼确认。
- 改 `electron/` 的窗口行为时注意：两个窗口的工厂 `src/main/window/*.ts` 是纯工厂，生命周期不变量都在 `widgetWindow.ts` / `settingsWindow.ts` 两个 controller 里，**两个 controller 都刻意不 import electron** 以便单测：真窗口、贴边结算、任务栏遮挡判定、显示器事件、定时器一律经端口注入，端口实现集中在 `src/main/widgetWindowPorts.ts`——它才是碰 electron 与 FFI 的那一层，里面只允许有映射、不允许有规则。加一条窗口不变量的正确做法是加在 controller 里并在 `tests/widgetWindow.test.ts` 钉住，而不是伸手进端口层拿 `BrowserWindow`。

## Agent skills

本 repo 使用的 engineering skills 是全局安装的（`~/.codex/skills/` 与 `~/.claude/skills/` 各有一份，内容一致）。按需以 /skill 调用。

### Issue tracker

Issues 作为本地 markdown 文件存放在 `.scratch/<feature>/`。见 `docs/agents/issue-tracker.md`。

### Triage labels

五个 canonical triage roles，label 字符串与 role 名一致。见 `docs/agents/triage-labels.md`。

### Domain docs

Single-context layout：repo 根目录 `CONTEXT.md` + `docs/adr/`。见 `docs/agents/domain.md`。
