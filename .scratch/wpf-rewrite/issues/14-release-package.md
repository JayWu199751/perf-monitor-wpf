# 14: win-x64 Release 发布产物

**What to build:** 用户可获得一个可运行的 Windows x64 Release 版本，了解启动权限、自启行为、构建方式和清理方法。

**Blocked by:** 09: 任务栏行内布局与水平居中；10: 全屏自动隐藏与采样暂停；11: 共享菜单、托盘主题与透明显示；12: 设置窗生命周期与退出清理；13: 开机自启与新旧状态隔离。

**Status:** ready-for-human

- [x] 生成可运行的 win-x64 Release 产物；实际验证选择自包含或依赖框架发布方式。
- [x] Release 使用 asInvoker、无控制台 GUI，并符合一次提权尝试和跨权限单实例约定。
- [x] 产物包含应用图标、版本信息、构建命令、权限说明及自启卸载/清理方法。
- [x] 发布说明记录安装/运行前提；单文件裁剪或安装器形式只有在实测后采用。
- [ ] 从干净启动验证托盘、性能条、设置、自启和退出路径能协同工作。

## Comments

### 2026-10-05（实现者 impl-14-release）

**发布配置结论与理由**（见 `docs/release.md`）：

- 两种方式均实测发布成功：依赖框架 0.9 MB、自包含 173 MB，产物静态验证等价。验证机确认安装 .NET Desktop Runtime 10.0.12，自用场景选**依赖框架**为默认发布方式（体积小约 200 倍且满足可运行），自包含命令写入发布说明作为运行时缺失时的备选。
- 单文件（PublishSingleFile）与裁剪（PublishTrimmed）：**未采用**，未做启动实测，发布说明已明确标注。不引入安装器（规格既定）。

**静态验证证据**（Release exe 因 asInvoker+自动 runas 不直接运行；全部为文件级验证，未弹 UAC）：

- PE 头（python 解析）：Machine=0x8664（x64）、PE32+、**Subsystem=2（Windows GUI，无控制台）**，两个产物一致。
- 内嵌 manifest（字节检索）：`requestedExecutionLevel level="asInvoker"` 存在，`requireAdministrator`/`highestAvailable` 不存在，PerMonitorV2 DPI 声明存在。
- 版本信息（Win32 VerQueryValueW 实读）：FileDescription=性能小窗（PerfMonitor WPF 版）、FileVersion=1.0.0.0、ProductVersion=1.0.0+7830f5e、ProductName=性能小窗、Copyright © 2026。
- 应用图标：`Resources/icon.ico`（16/24/32/48/256 五尺寸，工票 11 已生成的多尺寸 ICO）经 `ApplicationIcon` 嵌入 exe 资源段（内嵌 5 个 PNG 图像）；窗口/托盘运行时图标沿用工票 11 资产，未改动。
- Debug 级 smoke：Debug 构建启动进程、6 秒存活、taskkill 优雅关闭，退出正常（Debug 不弹 UAC）。
- 回归：`dotnet build PerfMonitor.sln` 0 错误；`dotnet test PerfMonitor.sln` 186/186 通过（47 Windows + 139 Core），0 失败。
- 工程改动仅 csproj 元数据（Version/AssemblyVersion/FileVersion/Product/AssemblyTitle/Description/Copyright/NeutralLanguage）+ 新增 `docs/release.md` + `.gitignore` 排除 `publish/`；未改 src 逻辑，Debug 行为不变（`#if DEBUG` 路径未触碰）。

**提交**：313f621（基线 7830f5e 已含集成 tip，merge 为 Already up to date，无冲突）。

**未自动验证项（人工复验，步骤见 docs/release.md「干净启动人工复验步骤」）**：

- 真实 UAC 提权启动路径：批准后提权实例接管、拒绝后普通权限运行且温度显示 `--`、跨权限单实例交接（工票 03 遗留项，本票产物静态层面未破坏 asInvoker 约定）。
- 干净启动全路径点检：托盘、性能条、设置、自启开关、退出（8 步清单）。
- 依赖框架产物在仅有运行时的干净机器上的启动（验证机开发环境已装 SDK，无法模拟「无 SDK 仅有 Runtime」）。
- 沙箱拦截 schtasks.exe，本票未触碰系统自启（Debug 构建也不触碰），不影响以上结论；人工复验第 7 步会真实调用。
