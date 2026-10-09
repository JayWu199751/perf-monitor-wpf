# 20: 设置窗外框圆角

Status: ready-for-human

## 问题与范围

用户于 2026-10-09 要求增加设置界面边框圆角，授权代理指定大小。采用四角 12 DIP：比小控件更舒展，同时适合固定 380 DIP 宽的设置窗。

## 实施

- 设置窗沿用现有 WindowChrome，外框圆角显式设为 12 DIP。
- 玻璃边框厚度设为 0，让 WindowChrome 的 CornerRadius 生效，并由原生窗口区域裁切背景。
- 沿用现有主题背景、头部拖动、关闭按钮、滚动和设置窗生命周期。
- 同步 UI 参数；不增加用户设置或新的圆角配置字段。

## 验收

- [x] Release 构建及现有应用测试通过。
- [ ] 真机检查深浅主题四角、边缘与阴影观感；确认拖动、关闭和小屏滚动正常。

## Comments

- 2026-10-09：开始实施。查阅微软 WindowChrome.CornerRadius 文档，确认在 DWM 启用时需将 GlassFrameThickness 设为 0 才能显式控制圆角；直接调整现有框架，避免仅绘制圆角背景却留下方形窗口区域。
- 技术依据：[WindowChrome.CornerRadius 官方文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome.cornerradius?view=windowsdesktop-10.0)。
- 2026-10-09：Release 构建 0 警告、0 错误；现有应用测试 18 项通过，包含屏幕外真实设置窗创建、重开与重建。实际桌面四角与阴影观感仍待人工确认，未将自动化结果等同于人工验收。

### 2026-10-09：生成手动安装包

- Release / win-x64 依赖框架发布与 Inno Setup 编译成功，包含设置窗 12 DIP 圆角修改。
- 发布目录应用 DLL 与本次构建产物 SHA-256 一致。
- 安装包：C:\Users\10854\Code\PerfMonitor-WPF\installer\Output\PerfMonitorWpf-1.0.0-setup.exe
- 大小：2294427 字节；SHA-256：A4590EFB8531A4E0C3C8651E2B473F2C4D815F424FD688EF04EB435EDEF4CDC9
- 未执行安装，交由用户手动安装。
