# 01：应用启动壳与托盘入口

**交付内容：** 用户在 Windows 11 上启动后能看到一个简洁的性能条窗口和托盘图标，可显隐性能条、打开基础设置窗口并正常退出。

**阻塞项：** 无（可立即开始）。

**Status:** resolved

- [x] Debug 以 GUI 方式启动，不出现控制台窗口，只创建一个性能条窗口和一个托盘图标（真实 WinExe smoke 确认 GUI 子系统与 1 个可见性能条窗口；NotifyIcon 初始化代码路径执行，托盘外观待人工确认）。
- [x] 自动显示性能条时不抢占前台焦点；窗口置顶且没有标题栏和任务栏按钮（PID 未成为前台，HWND 无标题栏且置顶；任务栏按钮仍待人工确认）。
- [x] 托盘左键单击可切换性能条显隐（Core 行为合同已验证，NotifyIcon 实际输入待人工验收）。
- [x] 托盘和性能条右键可打开包含“打开设置”和“退出”的基础菜单；设置项能打开一个可关闭的基础设置窗口（Core 行为合同已验证，WPF 桌面菜单/设置窗操作待人工验收）。
- [x] 用户可从基础菜单退出；退出后窗口、托盘图标和应用进程均结束（WM_CLOSE 关闭路径已使进程以退出码 0 结束；菜单入口及托盘资源需人工验收）。

## Comments

- 2026-10-05：已从集成基线 `250921e` 创建分支 `codex/wpf-shell-01`。新增 `global.json` 固定 .NET SDK 10.0.401；本机已安装该 SDK 和 .NET/WPF 10.0.12 runtime，Debug WPF 工程构建成功。运行时无第三方依赖；托盘使用 .NET Windows Desktop 自带 WinForms `NotifyIcon`。测试依赖固定为 Microsoft.NET.Test.Sdk 17.14.1、xunit 2.9.3、xunit.runner.visualstudio 3.1.4。
- TDD 首个行为先运行失败，再实现启动壳合同；后续托盘显隐、共享菜单、设置开关、退出行为均先红后绿。自动化测试只经过 Core `StartupShellController` 用户可见行为合同与窗口能力端口，未读取 XAML 属性。
- 通过：`dotnet test PerfMonitor.sln --no-restore`（6 项通过）；`dotnet build PerfMonitor.sln --no-restore`（0 警告，0 错误）。已确认 `bin/`、`obj/` 和 `.vs/` 生成物由根 `.gitignore` 忽略。
- 真实 Debug WinExe smoke：PE Subsystem=2（Windows GUI）；按 PID 用 EnumWindows 找到一个可见“性能小窗” HWND，主窗标题栏样式位为 0、扩展样式为 `0x00080008`（WS_EX_LAYERED 与 WS_EX_TOPMOST），启动后应用 PID 未成为前台；发送 WM_CLOSE 后进程在 5 秒内以退出码 0 结束。`Process.MainWindowHandle` 对 `ShowInTaskbar=False` 的窗口返回 0，因此 smoke 用 EnumWindows 枚举 PID 窗口。WPF 主窗有隐藏 owner；任务栏上实际是否出现按钮未直接观察，保留人工验收。
- 待人工验收：请在 Windows 11 Debug 启动后检查任务栏按钮与托盘图标；实际点托盘左键切换显隐，从托盘和性能条分别右键打开菜单，打开并关闭设置，再从菜单退出并确认窗口、托盘图标和进程全部结束。当前环境未执行这些托盘/菜单鼠标操作，因此相应 checklist 保持未勾选；Core 合同测试不作为 WPF 鼠标交互证据。


### 2026-10-10：用户人工验收通过

- 用户明确确认‘除了内存我已全部验证通过’。本事项非内存验收已通过，状态更新为 `resolved`。
- 证据为用户整体人工确认，详见 [验收记录](../acceptance-2026-10-10.md)；未补写逐项操作过程或测量数据。正文及旧 Comments 中‘待人工验收/未验证’等表述保留为历史情况，以本次确认更新当前结论。
