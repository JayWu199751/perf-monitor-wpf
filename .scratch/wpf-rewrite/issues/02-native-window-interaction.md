# 02: 原生性能条窗口交互与 DPI

**What to build:** 性能条作为原生 WPF 悬浮窗口，在不同缩放比例下可见、可命中并可顺畅拖动。

**Blocked by:** 01: 应用启动壳与托盘入口。

**Status:** ready-for-human

- [x] 窗口采用无标题栏的透明宿主，不产生任务栏按钮，并保持在其他普通窗口上方。
- [x] 用户从可见内容或视觉透明角落拖动时，整窗通过 WPF `DragMove` 跟手移动；真实 WPF 窗口上的透明角落命中、拖动已烟测。
- [ ] 窗口在目标 DPI 和混合 DPI 显示器上保持正确尺寸、命中区域与位置；当前仅在单屏 150% DPI 烟测，混合 DPI 切换待复验。
- [x] 性能条右键可打开基础菜单，不会启动拖动；真实 WPF 窗口的视觉透明角落右键能打开菜单。

## 混合 DPI 复验步骤

1. 在具有不同缩放比例（例如 100% 与 150%）的两台显示器上启动应用，并确认窗口的 DPI awareness context 为 PerMonitorV2。
2. 在各屏记录 `GetDpiForWindow` 和 PerMonitorV2 线程上下文下的 `GetWindowRect` 物理矩形；与 WPF DIP 尺寸按 `physical px = DIP × DPI / 96` 对照，记录整数取整差。
3. 从文字区和视觉透明角落分别拖过屏幕边界，确认每屏 `WindowFromPoint` 都命中性能条 HWND、窗口无位置跳变且尺寸/命中区域随 DPI 正确更新。
4. 在目标屏上对透明角落右键，确认弹出基础菜单且原生窗口矩形不变；再往返切屏并重复拖动。
5. 记录 Windows build、两屏缩放比例、DPI/矩形值和操作结果；缺少第二台不同 DPI 的显示器时保持本项未勾选。

## Comments

- 2026-10-05：完成 Core `StartupShellController` 行为合同接线。性能条左键移动请求经过 controller 后调用 host 的原生移动能力；右键沿用 controller 菜单入口，二者不共用拖动触发。
- 2026-10-05：真实 WPF smoke（Windows 11 专业版 10.0.26300，.NET 10.0.12；当前仅 `DISPLAY1`，`GetDpiForWindow=144`，即 150%）。`GetWindowDpiAwarenessContext` 与 PerMonitorV2 context 比较为真；PerMonitorV2 线程上下文读得初始窗口物理矩形 455×57 px。窗口 HWND 的 `WS_EX_LAYERED` 与 `WS_EX_TOPMOST` 已置位；UI Automation 在 Shell taskbar 下未找到“性能小窗”按钮。
- 2026-10-05：Alpha=0 的圆角像素实测 `WindowFromPoint` 会命中下层桌面窗口。将窗口背景设为 Alpha=1 后，同一视觉透明角落命中性能条 HWND；从该点注入左键拖动，窗口矩形随指针移动；从该点右键，菜单 popup HWND 出现在鼠标位置附近且主窗口矩形不变。Alpha=1 的依据是上述真实 HWND 命中与拖动观察；混合 DPI 尚未验证。
- 自动化验证：`dotnet test tests/PerfMonitor.Core.Tests/PerfMonitor.Core.Tests.csproj --no-restore --verbosity minimal`（7 项通过）；`dotnet build src/PerfMonitor.App/PerfMonitor.App.csproj --no-restore --verbosity minimal`（0 警告、0 错误）。
