# 性能小窗 · PerfMonitor WPF

适用于 Windows 11 的桌面性能悬浮条，使用 C# 和 WPF 构建。

## 功能

- 显示 CPU、内存、NVIDIA GPU、网络上传/下载速度及时间，可选择显示的指标。
- 支持深浅主题、字号、透明度、位置吸附和多显示器位置恢复。
- 支持托盘菜单、全屏自动隐藏、隐藏时暂停采样及开机自启。
- 设置窗关闭后隐藏，闲置后回收；配置保存在当前用户目录。

部分温度及 GPU 指标取决于硬件、驱动与权限，不可用时显示 `--`。

## 下载安装

1. 从 [Releases](https://github.com/JayWu199751/perf-monitor-wpf/releases/latest) 下载 `PerfMonitorWpf-1.0.0-setup.exe`。
2. 安装并启动应用。系统要求为 **Windows 11 x64**，且已安装 **.NET Desktop Runtime 10.0（x64）**。
3. 通过性能条或托盘右键菜单打开设置。

安装器按当前用户安装，默认目录为 `%LOCALAPPDATA%\Programs\PerfMonitorWpf`。
Release 应用启动时会尝试请求管理员权限；拒绝后仍可使用，部分温度读数可能不可用。
卸载前请在设置中关闭开机自启。

## 从源码构建

需要 Windows 和 .NET SDK 10.0.401（或兼容补丁版本，见 `global.json`）。在仓库目录使用 PowerShell：

```powershell
dotnet restore PerfMonitor.sln
dotnet build PerfMonitor.sln -c Release --no-restore
dotnet test PerfMonitor.sln --no-restore
```

发布依赖框架的版本：

```powershell
dotnet publish src/PerfMonitor.App/PerfMonitor.App.csproj -c Release -r win-x64 --self-contained false -o publish/fdd
```

如需无需单独安装 .NET 运行时的版本，将 `--self-contained false` 改为 `--self-contained true`。
安装包使用 Inno Setup 6 构建，步骤见[发布说明](docs/release.md)。

## 项目结构

- `src/PerfMonitor.Core`：设置、指标计算与行为规则。
- `src/PerfMonitor.Windows`：Windows 指标、显示器、托盘与自启适配。
- `src/PerfMonitor.App`：WPF 性能条和设置界面。
- `tests`：自动化测试。
- `docs`：设计、发布与验收文档。

本项目为个人工具，已完成用户实际使用验收，记录见[验收说明](.scratch/wpf-rewrite/acceptance-2026-10-10.md)。
