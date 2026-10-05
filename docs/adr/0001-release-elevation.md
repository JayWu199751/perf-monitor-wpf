# 发布版采用可继续普通运行的一次提权路径

Status: accepted

Release 以 asInvoker 启动；每次尚未提权的 Release 启动最多自动尝试一次 runas，同一次启动中不重试。用户拒绝或提权启动失败时，当前实例继续以普通权限运行，温度读数显示为缺失。该选择保留旧版启动时请求管理员权限的体验，同时避免 requireAdministrator manifest 在拒绝 UAC 时无法启动当前实例；跨权限实例必须交接为单实例。

## 实现说明（2026-10-05）

- 单实例对象限定在当前交互会话：互斥体名为 `Local\PerfMonitorWpf.SingleInstance.<sessionId>`，管道名称也包含当前 Session ID。对象名不包含用户 SID，因此同一会话内使用其他管理员账号运行的 runas 子进程可以命中相同对象。互斥体和管道的安全描述符使用 `D:(A;;GA;;;IU)S:(ML;;NW;;;LW)`：DACL 显式向 Interactive SID 授予 Generic All；SACL 标记 Low 完整性并禁止低完整性写入。管道启用 `PIPE_REJECT_REMOTE_CLIENTS`，没有向 Everyone 或远程客户端开放。
- 本地管道每个连接只处理一条 ASCII 命令，仅接受 `SHOW` 或带随机一次性 GUID 的 `TAKEOVER|<nonce>`；单行上限 256 字节，交接 nonce 5 分钟后过期。客户端请求/响应有 5 秒总时限，服务端单请求读取有 2 秒时限，提权接管等待旧实例释放互斥体最多 30 秒。
- 用户配置目录按实际进程用户解析。同一用户 SID 下的普通进程与提升进程使用同一配置；OTS 输入另一管理员账号会得到另一 Windows 用户 SID，因此其配置归属不在“同一用户”保证内。本次未验证替代凭据情形。
- 取消 UAC、批准后的跨权限交接及权限边界下的重复启动没有在实现期间实际触发，仍需按事项 03 的复验步骤验证；不得将这些路径记为已通过。
