// Win32 查询边界：前台窗口信息、任务栏遮挡判定、进程令牌提权状态。集中放这里，其余模块不直接碰 koffi。
import koffi from 'koffi';
import type { Edges } from './geometry';

const RECT = koffi.struct('RECT', {
  left: 'int32_t',
  top: 'int32_t',
  right: 'int32_t',
  bottom: 'int32_t'
});

// WindowFromPoint 按值接收 POINT
const POINT = koffi.struct('POINT', { x: 'int32_t', y: 'int32_t' });

// TOKEN_ELEVATION 只有一个 DWORD 字段，直接按结构体读回提权位
const TOKEN_ELEVATION = koffi.struct('TOKEN_ELEVATION', { TokenIsElevated: 'uint32_t' });

const GWL_STYLE = -16;
const GA_ROOT = 3; // GetAncestor 的"取所属顶层窗口"
const TASKBAR_CLASS = 'Shell_TrayWnd';
const TOKEN_QUERY = 0x0008;
const TokenElevationClass = 20; // TOKEN_INFORMATION_CLASS.TokenElevation

// koffi 把原生指针/内存块以 any 暴露，起个名字，避免满屏 any
type KoffiPtr = ReturnType<typeof koffi.alloc>;

const user32 = koffi.load('user32.dll');
const kernel32 = koffi.load('kernel32.dll');
const advapi32 = koffi.load('advapi32.dll');

// 局部绑定与导出函数刻意不同名：同名会让 TS 把它们当成合并声明（同名函数+变量）而报错。
const getFgWindow = user32.func('GetForegroundWindow', 'void*', []);
const getWindowRect = user32.func('GetWindowRect', 'bool', [
  'void*',
  koffi.out(koffi.pointer(RECT))
]);
const getClassNameW = user32.func('GetClassNameW', 'int32_t', ['void*', 'char16_t *', 'int32_t']);
const getWindowLongPtrW = user32.func('GetWindowLongPtrW', 'int64_t', ['void*', 'int32_t']);
const findWindowW = user32.func('FindWindowW', 'void*', ['const char16_t*', 'const char16_t*']);
const windowFromPoint = user32.func('WindowFromPoint', 'void*', [POINT]);
const getAncestor = user32.func('GetAncestor', 'void*', ['void*', 'uint32_t']);

const getCurrentProcess = kernel32.func('GetCurrentProcess', 'void*', []);
const openProcessToken = kernel32.func('OpenProcessToken', 'bool', [
  'void*',
  'uint32_t',
  koffi.out(koffi.pointer('void*'))
]);
const closeHandle = kernel32.func('CloseHandle', 'bool', ['void*']);
const getTokenInformation = advapi32.func('GetTokenInformation', 'bool', [
  'void*',
  'int32_t',
  'void*',
  'uint32_t',
  'void*'
]);

export interface ForegroundWindow {
  rect: Edges;
  className: string;
  // GWL_STYLE 原始值：全屏判定据此排除带标题栏/可缩放边框的普通窗口
  style: number;
}

// 返回前台窗口的屏幕坐标 rect（物理像素）、窗口类名与样式；无前台窗口时返回 null。
export function getForegroundWindow(): ForegroundWindow | null {
  const hwnd = getFgWindow() as bigint | null;
  if (!hwnd) return null;
  const rect = {} as Edges;
  if (!getWindowRect(hwnd, rect)) return null;
  const buf = new Uint16Array(128);
  getClassNameW(hwnd, buf, buf.length);
  const end = buf.indexOf(0);
  const className = String.fromCharCode(...buf.slice(0, end === -1 ? buf.length : end));
  const style = Number(getWindowLongPtrW(hwnd, GWL_STYLE) as bigint);
  return { rect, className, style };
}

// 当前进程是否以提升（管理员）令牌运行。
//
// 为什么不用 `net session`：它依赖 Workstation/Server 服务，服务被禁用时管理员进程
// 也会误判为未提权，且每次调用闪一次控制台窗口。查进程令牌的 TokenElevation 是唯一
// 可靠且零副作用的读法。
//
// 非 Windows 平台无 UAC 概念，一律视为已提权，调用方无需再判 process.platform。
export function isTokenElevated(): boolean {
  if (process.platform !== 'win32') return true;
  // koffi 的 out 参数：koffi.out() 只在函数声明里作类型标记，调用时直接传
  // koffi.alloc() 返回的裸指针（bigint），再 koffi.decode() 读回。
  let tokenSlot: KoffiPtr | null = null;
  let infoSlot: KoffiPtr | null = null;
  let returnLen: KoffiPtr | null = null;
  let token: bigint | null = null;
  try {
    tokenSlot = koffi.alloc('void*', 1);
    if (!openProcessToken(getCurrentProcess(), TOKEN_QUERY, tokenSlot)) return false;
    token = koffi.decode(tokenSlot, 'void*') as unknown as bigint;
    if (!token) return false;

    infoSlot = koffi.alloc(TOKEN_ELEVATION, 1);
    returnLen = koffi.alloc('uint32_t', 1);
    const ok = getTokenInformation(
      token,
      TokenElevationClass,
      infoSlot,
      koffi.sizeof(TOKEN_ELEVATION),
      returnLen
    );
    if (!ok) return false;
    const info = koffi.decode(infoSlot, TOKEN_ELEVATION) as { TokenIsElevated: number };
    return info.TokenIsElevated !== 0;
  } catch {
    // FFI 不可用（如 koffi 原生模块未就位）时按未提权处理：宁可多问一次 UAC，
    // 也不要在真未提权时静默把 CPU 温度永久显示成 `--`
    return false;
  } finally {
    if (token) {
      try {
        closeHandle(token);
      } catch {
        // ignore
      }
    }
    // 接收内存交给 GC：koffi.alloc 的内存随句柄一起失效，句柄已 CloseHandle，不再读
    void tokenSlot;
    void infoSlot;
    void returnLen;
  }
}

// 任务栏根窗口句柄。调用方应当缓存它、按较低频率重取（explorer 重启会换句柄，
// 300ms 的重取自愈足够）：FindWindowW 每次都要把类名从 JS 字符串编组到 UTF-16，
// 放在 30ms 热路径上实测是 z-order 守卫开销的主要来源。
export function findTaskbarWindow(): bigint | null {
  if (process.platform !== 'win32') return null;
  try {
    return (findWindowW(TASKBAR_CLASS, null) as bigint | null) ?? null;
  } catch {
    return null;
  }
}

// 某个**物理像素**点是否归给定句柄（或其顶层祖先）。
//
// 用途：小窗停在任务栏行内时，explorer 会在用户点任务栏的那一刻把 Shell_TrayWnd
// 重新插回 topmost 带最前面，把小窗压到下面——透明窗被半透明任务栏盖住等于看不见。
// 只比句柄、不读类名，避开 AGENTS.md 记的"按地址解字符串会 AV"那条坑。
// 非 Windows 或 koffi 不可用时返回 false：宁可不动作，也不要盲目抢 z-order。
export function isPointOwnedByWindow(hwnd: bigint | null, x: number, y: number): boolean {
  if (process.platform !== 'win32' || !hwnd) return false;
  try {
    const hit = windowFromPoint({ x, y }) as bigint | null;
    if (!hit) return false;
    const root = getAncestor(hit, GA_ROOT) as bigint | null;
    return root !== null && root === hwnd;
  } catch {
    return false;
  }
}
