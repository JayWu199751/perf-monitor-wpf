# 计划任务自启用 schtasks.exe 与 XML 定义

Status: accepted

开机自启采用当前用户登录触发的最高权限计划任务，实现选用 `schtasks.exe` 子进程配合 XML 任务定义导入（`schtasks /Create /XML`），而不是直接 P/Invoke 任务计划程序 COM 接口（ITaskService 等）。理由：

- XML 定义把命令路径直接写入 `<Command>` 元素，绕开 `/TR` 命令行参数的引号转义问题，带空格路径天然正确；XML 转义交给标准 XML 序列化。
- XML 可以精确指定 LogonTrigger 限定当前用户（schtasks 交互式参数无法做到）、`RunLevel HighestAvailable`（最高权限）与 `ExecutionTimeLimit PT0S`（长驻应用不限时）。
- COM 方式需要维护大量 interop 类型并手工释放 COM 资源，出错面大；schtasks 子进程与现有提权启动器（WindowsRunAsLauncher）的子进程风格一致，且已验证 `UseShellExecute=false + CreateNoWindow` 不弹控制台窗口。

任务名使用独立于旧版命名空间的 `PerfMonitorWpf`（可注入，集成测试用 `PerfMonitorWpf.IntegrationTest.*`），只创建、查询和删除该名字下的任务。每次创建或删除后再用 `schtasks /Query` 确认系统实际状态，以查询结果回写设置，失败不报告成功。注册 `HighestAvailable` 任务需要提升令牌，普通权限实例中操作会失败并在设置界面如实显示失败；Release 版正常流程本就以提权实例为主实例。Debug 构建在应用装配层不提供自启端口（IAutostartPort 返回 null），设置窗开关禁用并注明"开发环境不可用"，保证开发时不触碰系统自启；adapter 本身不带 Debug 分支，以便集成测试验证真实行为。
