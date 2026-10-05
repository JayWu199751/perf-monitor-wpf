// 提权重启时携带的命令行标记：新进程据此跳过提权分支，防止 UAC 弹窗循环。
// 开机自启计划任务的 /TR 也带上它（登录时任务已以 HIGHEST 令牌启动，无需再提权），
// 因此放在共享层，由 elevate 与 autostart 同源引用。
export const ELEVATED_FLAG = '--elevated';
