# 20: 设置窗外框圆角

Status: resolved

## 问题与范围

用户于 2026-10-09 要求增加设置界面边框圆角，授权代理指定大小。采用四角 12 DIP：比小控件更舒展，同时适合固定 380 DIP 宽的设置窗。

## 实施

- 设置窗沿用现有 WindowChrome，外框圆角显式设为 12 DIP。
- 玻璃边框厚度设为 0，让 WindowChrome 的 CornerRadius 生效，并由原生窗口区域裁切背景。
- 沿用现有主题背景、头部拖动、关闭按钮、滚动和设置窗生命周期。
- 同步 UI 参数；不增加用户设置或新的圆角配置字段。

## 验收

- [x] Release 构建及现有应用测试通过。
- [x] 真机检查深浅主题四角、边缘与阴影观感；确认拖动、关闭和小屏滚动正常。

## Comments

- 2026-10-09 短时验收补充：引用发布版程序集和同一 manifest 在屏幕外创建两代真实设置窗，144 DPI 下宽 380 DIP，原生区域查询确认四个极角均被裁切、12 DIP 内部点保留；原生圆角裁切路径已证实。10 次隐藏重开和保存、销毁后 HWND 失效均通过。真实桌面阴影、边缘观感、鼠标拖动和短屏滚动仍待验收，详见 [验收报告](../acceptance-2026-10-09.md)。

- 2026-10-09：开始实施。查阅微软 WindowChrome.CornerRadius 文档，确认在 DWM 启用时需将 GlassFrameThickness 设为 0 才能显式控制圆角；直接调整现有框架，避免仅绘制圆角背景却留下方形窗口区域。
- 技术依据：[WindowChrome.CornerRadius 官方文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome.cornerradius?view=windowsdesktop-10.0)。
- 2026-10-09：Release 构建 0 警告、0 错误；现有应用测试 18 项通过，包含屏幕外真实设置窗创建、重开与重建。实际桌面四角与阴影观感仍待人工确认，未将自动化结果等同于人工验收。

### 2026-10-09：生成手动安装包

- Release / win-x64 依赖框架发布与 Inno Setup 编译成功，包含设置窗 12 DIP 圆角修改。
- 发布目录应用 DLL 与本次构建产物 SHA-256 一致。
- 安装包：C:\Users\10854\Code\PerfMonitor-WPF\installer\Output\PerfMonitorWpf-1.0.0-setup.exe
- 大小：2294427 字节；SHA-256：A4590EFB8531A4E0C3C8651E2B473F2C4D815F424FD688EF04EB435EDEF4CDC9
- 未执行安装，交由用户手动安装。


### 2026-10-10：用户人工验收通过

- 用户明确确认‘除了内存我已全部验证通过’。本事项非内存验收已通过，状态更新为 `resolved`。
- 证据为用户整体人工确认，详见 [验收记录](../acceptance-2026-10-10.md)；未补写逐项操作过程或测量数据。正文及旧 Comments 中‘待人工验收/未验证’等表述保留为历史情况，以本次确认更新当前结论。
