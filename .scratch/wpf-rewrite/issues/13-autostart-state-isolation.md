# 13: 开机自启与新旧状态隔离

**What to build:** 用户可从新版设置管理当前用户登录时的静默自启；新版与旧版配置及启动项并存时互不修改对方状态。

**Blocked by:** 03: Release 提权与跨权限单实例；07: 指标设置、持久化与性能条布局。

**Status:** ready-for-human

- [x] 设置中的自启状态对应当前用户登录触发的最高权限计划任务，路径和带空格参数正确转义，启动为静默 GUI。
- [x] 创建或删除任务后查询系统实际状态并回写；失败时设置界面不显示成功。
- [x] Debug 不创建或修改系统自启；新版只创建、查询和删除自己拥有的任务。
- [x] 新版使用独立的用户级配置目录和自启任务名；同一用户的普通/管理员进程读写同一新版配置。
- [x] 不读取、导入、迁移、改写或删除旧版配置，不扫描、迁移或删除旧版计划任务与 Run 项；旧版继续可用。

## Comments

### 2026-10-05（实现者 impl-13-autostart）

**实现摘要**

- Core：`StartupShell.cs` 新增 `IAutostartPort`（返回 `AutostartRequestOutcome`：Enabled/Disabled/Failed）与 `IStartupShellHost.AutostartPort` 可替换端口；`StartupShellController.UpdateSettings` 在 patch 携带 `Autostart` 时经端口执行意图，以端口回报的系统实际状态回写并持久化，`Failed` 抛 `InvalidOperationException`（不保存，设置界面显示"保存失败"）。
- Windows：新增 `ScheduledTasks/WindowsScheduledTaskAutostart.cs`。选型 schtasks.exe + XML 定义（`schtasks /Create /XML`，ADR 0005）：命令路径直写 `<Command>` 元素免命令行转义，带空格路径正确；XML 指定当前用户 LogonTrigger（`UserId`）、`RunLevel HighestAvailable`、`LogonType InteractiveToken`、`ExecutionTimeLimit PT0S`；无参数启动 exe 即静默 GUI。子进程 `UseShellExecute=false + CreateNoWindow + RedirectStandardOutput/Error`，15 秒超时杀进程树。创建/删除后均以 `schtasks /Query` 核实实际状态；任务名 `PerfMonitorWpf`（独立命名空间，可注入），只创建/查询/删除自己拥有的任务。
- App：`WpfStartupShellHost.AutostartPort` 在 Release 返回 adapter 实例；`#if DEBUG` 返回 null，设置窗开关禁用并显示"开发环境不可用"（既有 UI），Debug 构建不触碰系统自启。设置模型/持久化/设置窗的 `Autostart` 行（工票 07 已就位）无需改动。
- ADR：docs/adr/0005-scheduled-task-autostart-via-schtasks.md。
- 状态隔离验证：配置目录 `%LOCALAPPDATA%\PerfMonitorWpf`（按进程用户解析，`Environment.SpecialFolder.LocalApplicationData`，同用户提权前后 SID 相同，普通/管理员读写同一文件）；不读取/导入/迁移/改写/删除旧版配置、旧版计划任务与 Run 项——adapter 只按注入的任务名操作，无任何扫描逻辑。

**测试证据**

- `dotnet build PerfMonitor.sln`：0 警告 0 错误。
- `dotnet test PerfMonitor.sln`：69 通过 0 失败（Core.Tests 55 含 AutostartContractTests 5 例：端口请求传递、实际状态回写、失败不保存、无端口语义；Windows.Tests 14 含 adapter XML 生成 4 例与集成测试）。
- 集成测试（创建→查询→删除 `PerfMonitorWpf.IntegrationTest.*`，finally 清理）带两道前置：非提升令牌或 schtasks.exe 不可执行时跳过。**当前开发环境 schtasks.exe 被安全沙箱程序黑名单拦截，本次未真实执行**，复验方法见下。

**未自动验证项（人工复验步骤）**

1. **真实登录自启**：以 Release 构建运行 → 设置开启"开机自启"→ 确认提权实例中显示"已保存"→ `schtasks /Query /TN PerfMonitorWpf /XML` 核对触发器为当前用户 LogonTrigger、RunLevel HighestAvailable、Command 为 exe 路径 → 注销重登验证静默启动；再在设置中关闭，确认任务被删除且界面状态如实。也可直接以管理员运行 `dotnet test tests/PerfMonitor.Windows.Tests --filter FullyQualifiedName~Integration`（需提升 + schtasks.exe 不在沙箱黑名单）。
2. **普通权限实例失败路径**：在普通（非提权）实例设置窗开启自启，应显示"保存失败"且开关回滚（注册 HighestAvailable 任务被拒绝）。
3. **提权实例配置归属**：普通实例开启自启后（经提权交接）在提权实例中改其他设置，确认均写入 `%LOCALAPPDATA%\PerfMonitorWpf\settings.json` 同一文件（两实例同用户，无需换 SID）。
4. **旧版共存**：若机器装有旧版（Run 项/旧计划任务），开启新版自启后确认旧版启动项与配置原样存在，旧版仍可正常启动。
