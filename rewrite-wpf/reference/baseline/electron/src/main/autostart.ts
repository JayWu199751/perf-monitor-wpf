import { execFileSync } from 'node:child_process';
import { ELEVATED_FLAG } from '../shared/cli';

// 开机自启走计划任务而非 Run 键：
// - Run 键由用户 shell（中等完整性）在登录时拉取，manifest requireAdministrator 的 exe
//   无法启动且不弹 UAC（实测"自启不生效"）；
// - 计划任务由 Task Scheduler 服务在登录时以 HIGHEST 令牌直接创建进程 → 静默自启并
//   天然满足提权要求，登录无 UAC 弹窗。
// 任务名用 ASCII：避免 schtasks 输出/命令行解析的中文编码坑。
export const AUTOSTART_TASK_NAME = 'PerfWidgetAutostart';

const SILENT = { stdio: 'ignore', windowsHide: true } as const;

// TR 值形如 `"C:\Program Files\性能小窗\性能小窗.exe" --elevated`：
// 带引号保证带空格路径被 Task Scheduler 正确解析；--elevated 让任务万一降级运行时
// 跳过 UAC 重提权分支（避免登录弹窗循环）
export function buildEnableArgs(exePath: string): string[] {
  return [
    '/Create',
    '/TN',
    AUTOSTART_TASK_NAME,
    '/TR',
    `"${exePath}" ${ELEVATED_FLAG}`,
    '/SC',
    'ONLOGON',
    '/RL',
    'HIGHEST',
    '/F'
  ];
}

export function buildDisableArgs(): string[] {
  return ['/Delete', '/TN', AUTOSTART_TASK_NAME, '/F'];
}

// TR 值形如 `"C:\Program Files\性能小窗\性能小窗.exe" --elevated`：
export function enableAutostart(exePath: string = process.execPath): boolean {
  try {
    execFileSync('schtasks', buildEnableArgs(exePath), SILENT);
    return true;
  } catch (err) {
    console.error('enableAutostart failed:', err);
    return false;
  }
}

export function disableAutostart(): boolean {
  try {
    execFileSync('schtasks', buildDisableArgs(), SILENT);
    return true;
  } catch {
    // 任务不存在（/F 删除时）属正常，返回 false 表示"当前无自启任务"
    return false;
  }
}

// HKCU Run 键：setLoginItemSettings 时代的遗留自启项。名字形如 `electron.app.<app.name>`，
// 随 app.name 漂移（历史上有过 `electron.app.性能小窗`，未来可能变成别的），无法按固定名删除。
// 按 exe 路径匹配才是稳的：凡命令值以本应用 exe 开头的条目一律删。走 PowerShell 而非 `reg query`
// 解析，是因为值名含中文，`reg` 按 ANSI 输出会乱码（与 AUTOSTART_TASK_NAME 用 ASCII 同理避坑）。
export const RUN_KEY_PATH = 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run';

// 构造清理 HKCU Run 键残留自启项的 PowerShell 命令（纯函数，便于单测）。
// exe 路径以单引号嵌入 PS 字面量：单引号内 `$` 与反引号均按字面处理，仅需把 `'` 翻倍转义。
export function buildRunKeyCleanupCommand(exePath: string): string {
  const exeLiteral = exePath.replace(/'/g, "''");
  return [
    `$exe = '${exeLiteral}'`,
    `$key = '${RUN_KEY_PATH}'`,
    `$props = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue`,
    `if (-not $props) { exit 0 }`,
    `foreach ($name in $props.PSObject.Properties.Name) {`,
    `  if ($name -like 'PS*') { continue }`,
    `  $value = $props.($name)`,
    `  if ($value -is [string] and $value.Trim().Trim('"').StartsWith($exe, [System.StringComparison]::OrdinalIgnoreCase)) {`,
    `    Remove-ItemProperty -Path $key -Name $name -ErrorAction SilentlyContinue`,
    `  }`,
    `}`,
    `exit 0`
  ].join('; ');
}

export function cleanupLegacyRunEntry(exePath: string = process.execPath): boolean {
  try {
    execFileSync('powershell.exe', ['-NoProfile', '-Command', buildRunKeyCleanupCommand(exePath)], SILENT);
    return true;
  } catch (err) {
    console.error('cleanupLegacyRunEntry failed:', err);
    return false;
  }
}
