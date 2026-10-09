# 17: 托盘/性能条共享菜单亮暗主题化

**What to build:** 共享 WPF ContextMenu（托盘右键 + 性能条右键两入口）从系统默认样式改为应用自有视觉：对齐性能条 preview.html 色板（暗 #121519 底 / 亮 #F6F7F9 底），按 IsDarkEffectiveTheme 呈现亮暗两套外观，随主题设置与系统主题变化即时切换；菜单项内容与顺序不变。

**Blocked by:** 无（11 的共享菜单已落地）

**Status:** ready-for-human

决策记录（2026-10-07 与用户两轮烤问确认）：

- 范围：两入口一起改，共享单例语义不变；GLOSSARY 补「菜单」词条。
- 基调：性能条 preview.html 色板，浮层卡片语言（圆角 + 同款投影）。
- 主题：跟 `IsDarkEffectiveTheme()`（System/Dark/Light 三态解析），与托盘图标同源，单一判定来源。
- 载体：重绘 ContextMenu / MenuItem / Separator ControlTemplate，数据流、Placement=MousePoint、防重弹 guard 全部不动。
- 内容：6 项与顺序不动；checkable 勾选态用行首对勾；勾选行与动作项用正文色，未勾的 checkable 行用标签色。
- 材质：不透明底，不用亚克力/不跟随不透明度设置；性能条同款 DropShadowEffect（Blur 9 / Depth 2 / 270°，暗 0.32 / 亮 0.16），外围 16 DIP 透明留白容纳投影，PlacementOffset(-16,-16) 补偿命中与落点。
- 字号 13 固定（设置窗字体栈），不跟随性能条 FontSize 设置。

验收清单：

- [x] 参数表落实：暗 #121519 底/#F3F5F7 正文/#92979F 标签/白@0x0F hover/白@0x1A 边框/白@0x21 分隔；亮 #F6F7F9 底/#1A1D22 正文/#5F656D 标签/黑@0x0A hover/黑@0x1A 边框/黑@0x21 分隔。
- [x] 几何：容器圆角 10、容器 Padding 6、项高 32、项 hover 胶囊圆角 7 且左右留 4、字号 13、分隔线上下留 5 高 1。
- [x] 主题三态：System 跟随系统、Dark、Light 全部生效；系统主题变化时实时切换（复用托盘图标的刷新链路）。
- [x] STA 测试断言亮暗两套画刷、样式挂载与未勾行次级色引用。
- [x] 真机截图验收亮暗两态（对勾、hover、分隔线、投影、圆角）。

## Comments

### 2026-10-07（实现）

**实现摘要**

- 新增 `src/PerfMonitor.App/Shell/ShellContextMenuTheme.xaml`（模板 + 默认暗色画刷）与同名 `.xaml.cs`（`Apply(dark)` 逐键覆盖画刷，同设置窗 `ApplyTheme` 模式；`ApplyForeground` 定义勾选/动作行正文色与未勾行标签色）。
- `WpfStartupShellHost`：构造即挂皮肤；`ShowContextMenu` 弹出前兜底 `Apply(IsDarkEffectiveTheme())` 并为 Separator/MenuItem 挂资源引用；`RefreshTrayIcon` 的 dispatcher 回调里同步菜单主题（系统主题变化 → 托盘图标 + 菜单一起刷新）。数据流、Placement=MousePoint、防重弹 guard 未动。
- 模板：容器圆角 10 + 描边 + 性能条同款 DropShadowEffect（Blur 9 / Depth 2 / 270°，暗 0.32 亮 0.16），Grid Margin 16 透明留白容纳投影、背景 null 穿透命中；项高 32、hover 胶囊圆角 7（左右 Margin 4）、行首 16px 对勾槽（IsChecked 触发器显隐）、分隔线 1px 上下留 5；字号 13 固定（设置窗字体栈）。

**踩坑记录**

- `ContextMenu` 没有 `PlacementOffset`（那是 Popup 的属性），用 `HorizontalOffset/VerticalOffset`。且 MousePoint 模式下两轴语义不对称（亮度剖面实测标定）：Border 左缘 = 鼠标 x + HorizontalOffset + 18，Border 顶 = 鼠标 y + VerticalOffset（不叠加模板 16 留白）。最终 `HorizontalOffset=-16`、`VerticalOffset=0`，菜单左上角落点 ≈ 鼠标点，符合 Windows 右键菜单落点惯例。
- `UseWindowsForms=true` 使 `Color` 与 `System.Drawing.Color` 二义，code-behind 需 `using Color = System.Windows.Media.Color`。

**测试证据**

- 新增 `tests/PerfMonitor.App.Tests/ShellContextMenuThemeTests.cs`（5 例 STA）：暗色令牌、亮色令牌、样式/偏移挂载、前景引用随勾选态解析、Apply 幂等与翻转。全绿：Windows 46 / App 21 / Core 161（`WindowsScheduledTaskAutostartTests` 的 1 例失败为基线预存的环境性失败，已 stash 验证与本工票无关）。
- 截图验收（`.scratch/ui-restyle/menu-{dark,light}{,.hover}.png`，脚本 `menu-capture.ps1` 经 settings.json `theme` 字段切态）：亮暗两套色板、圆角 10 无残角、行首对勾、未勾行标签色、hover 胶囊、分隔线、四周投影全部符合参数表；左上角四倍放大复核无系统层方角残留。
