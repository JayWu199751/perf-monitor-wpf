import { spawn } from 'node:child_process';
import { ELEVATED_FLAG } from '../shared/cli';

export { ELEVATED_FLAG };

// 与打包侧 build.win.requestedExecutionLevel="requireAdministrator" 互补：
// manifest 让安装后的 exe 在进程创建前就被系统强制提升（覆盖打包场景）；
// 本模块的运行时提权兜底 dev 模式与任何未嵌入 manifest 的 exe。

// 管理员检测由调用方注入（组合根传 win32.isTokenElevated）：
// 本模块因此零原生依赖，buildRelaunchCommand / isElevated 可在 vitest 里直接跑。
// 检测本身走进程令牌的 TokenElevation，不用 `net session`——后者依赖 Server 服务，
// 服务禁用时管理员也会误判失败，且每次调用闪控制台窗口。
export type ElevatedProbe = () => boolean;

export function isElevated(probe: ElevatedProbe): boolean {
  if (process.platform !== 'win32') return true;
  return probe();
}

// PowerShell 双引号字符串内：` 为转义符，$ 为变量前缀，均需反引号转义
function psEscape(s: string): string {
  return s.replace(/`/g, '``').replace(/"/g, '`"').replace(/\$/g, '`$');
}

// 构造经 UAC 以管理员身份重启的 PowerShell 命令（纯函数，便于单测）。
// -ArgumentList 每个元素用双引号包裹、逗号连接：带空格路径会被 PowerShell 正确解析为一个参数。
export function buildRelaunchCommand(exe: string, args: string[], cwd: string): string {
  const argList = args.map((a) => `"${psEscape(a)}"`).join(',');
  return `Start-Process -FilePath "${psEscape(exe)}" -ArgumentList ${argList} -WorkingDirectory "${psEscape(cwd)}" -Verb RunAs`;
}

export interface RelaunchResult {
  /** 用户确认 UAC，提权实例已启动 → 调用方应退出当前（非管理员）实例 */
  onGranted: () => void;
  /** 用户拒绝 UAC 或提权命令执行失败 → 调用方以降级（非管理员）方式继续运行 */
  onDenied: () => void;
}

// 经 UAC 提权重启自身，结果异步回调。Start-Process -Verb RunAs 会阻塞直到用户响应 UAC：
// - PowerShell 退出码 0 = 提权实例已成功启动 → onGranted
// - 非 0（如 1223 用户取消）= 提权未生效 → onDenied
export function relaunchElevated(result: RelaunchResult): void {
  const args = process.argv.slice(1);
  if (!args.includes(ELEVATED_FLAG)) args.push(ELEVATED_FLAG);
  const cmd = buildRelaunchCommand(process.execPath, args, process.cwd());
  // windowsHide：避免提权前闪 PowerShell 控制台窗口（UAC 弹窗由系统绘制，与此无关）
  const child = spawn('powershell.exe', ['-NoProfile', '-Command', cmd], {
    stdio: 'ignore',
    windowsHide: true
  });
  child.on('error', () => result.onDenied());
  child.on('exit', (code) => {
    if (code === 0) result.onGranted();
    else result.onDenied();
  });
}
