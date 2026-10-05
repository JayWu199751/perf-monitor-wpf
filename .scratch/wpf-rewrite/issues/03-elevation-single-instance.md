# 03: Release 提权与跨权限单实例

**What to build:** Release 在需要时尝试以管理员权限运行温度读取，同时保证拒绝提权仍可使用，普通与管理员启动不会形成多个实例。

**Blocked by:** 01: 应用启动壳与托盘入口。

**Status:** ready-for-human

- [x] Core 用户可见启动合同：Debug 不提出权；Release 普通启动在未带尝试标记时只输出一次提权决策，带标记或真实 elevated 状态不重试；普通/管理员重复启动和合法/非法交接的决策符合单实例要求；重复启动要求显示并激活隐藏的性能条。
- [ ] Windows Release 验收：已实现真实 TokenElevation 读取和单次 `runas` 启动；批准/取消 UAC、ShellExecute 启动失败后的普通实例保留、本地配置归属尚未实际验证。
- [ ] Windows 单实例验收：已实现当前会话互斥体、Interactive SID ACL、本地 IPC 与一次性交接；Debug 同令牌双启动 smoke 通过，但真实窗口激活及普通↔管理员跨权限通知/接管尚未验证。
- [ ] 温度降级：本事项未实现 CPU 温度源，普通权限温度显示 `--` 尚未验证。

## Comments

- 2026-10-05：基于集成提交 `198ac7c` 创建分支 `codex/wpf-elevation-03`。启动策略和重复启动手动唤起合同并入 `StartupShellController`；Win32 token、mutex、受限 pipe IPC 与 runas adapter 暂放 `src/PerfMonitor.App/Platform/`。只修改 `App.xaml.cs` 做启动组装；未修改 `app.manifest`，未创建或修改 `PerfMonitor.Windows` 工程。
- Core 合同通过：`dotnet test PerfMonitor.sln --no-restore --verbosity minimal`（15 项）。Debug 与 Release 构建通过（均 0 警告、0 错误）。Debug WinExe 普通启动 smoke 通过：确认当前 Session ID 管道已创建，第二次启动在 264 ms 内以退出码 0 结束，主 PID 保持运行；未执行 runas，也未触发 UAC。首次 smoke 暴露 UI 线程同步等待异步 pipe I/O 的死锁，加入不捕获 UI 上下文后复测通过。smoke 只确认同当前令牌下的进程协调，不等于确认性能条真实可见/激活或跨权限 ACL。
- IPC 细节：互斥体名为 `Local\PerfMonitorWpf.SingleInstance.<sessionId>`，管道名也带当前 Session ID；二者均不含用户 SID，因此同一会话里以其他管理员账号运行的 runas 子进程也能命中相同对象。SDDL 为 `D:(A;;GA;;;IU)S:(ML;;NW;;;LW)`，DACL 显式授予 Interactive SID 的 Generic All，SACL 标注 Low 完整性并禁止低完整性写入。管道设置 `PIPE_REJECT_REMOTE_CLIENTS`。协议仅接受每连接一条 ASCII `SHOW` 或 `TAKEOVER|<nonce>` 命令，单行最多读取 256 字节；随机 GUID nonce 只接受一次且 5 分钟后过期。客户端往返最多等待 5 秒，服务端单请求最多读取 2 秒，接管等待旧实例释放互斥体最多 30 秒；未配置 Everyone 或远程管道访问。
- 配置归属：配置目录继续按实际进程用户解析；普通进程与以同一 SID 提升的进程共用用户配置。OTS 输入另一管理员账号会形成另一 Windows 用户 SID，其配置归属不属于“同一用户”保证；本轮未验证该凭据场景。
- 未验证及复验步骤：在 Windows 11 上用 Release `asInvoker` 启动，批准 UAC 后确认原 PID 退出、新 PID elevated 且只有一个托盘/性能条；再次从普通方式启动确认新进程通知 elevated 实例后退出。重新启动 Release 并取消 UAC，确认原普通实例继续、无新实例；随后以管理员方式重复启动并确认通知普通实例后退出。另在可控启动错误条件下验证 runas 失败仍保留普通实例；本轮没有触发 UAC，也没有模拟 ShellExecute 启动失败。用 OTS 另一管理员账号重复批准路径时，确认两个进程处于同一 session、成功命中同名 IPC，再确认各自按实际 SID 解析配置。待温度事项接入后，再确认普通权限下 CPU 温度显示 `--`。
