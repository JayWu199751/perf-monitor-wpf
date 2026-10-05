/**
 * z-order 守卫：停在**任务栏行内**的小窗被 explorer 反超后自动捞回来。
 *
 * 机制与被否决的方案在 ADR-0005「后续修正一」：点任务栏图标切应用那一刻，`Shell_TrayWnd`
 * 被重新插到 topmost 带前面，小窗从此永久压在任务栏半透明材质之下，而**本窗口收不到任何通知**
 * （被反超时一次 `WM_WINDOWPOSCHANGED` 都没有），所以只能轮询。换更高档不是解法（同带，只改概率）。
 *
 * 这个 module 存在的全部理由是「轮询间隔就是用户看到的闪烁时长」与「每拍的工作量就是 CPU 开销」
 * 这两条实测结论，它们此前内联在 widgetWindow.ts 里、被 electron import 挡着，一条都测不了：
 *  - 30ms（≈两帧）压的是闪烁时长，实测恢复 13ms / 20ms；
 *  - 行内判定与任务栏句柄按 `refreshTicks` 慢刷：句柄重取要 `FindWindowW` 编组类名字符串，
 *    放在 30ms 热路径上实测是开销主因（2.92% → 1.98% 单核）；
 *  - **不在行内就早退**，连遮挡判定都不问（悬浮态被盖住是正常 z-order，不该去抢；
 *    JS 会先求值全部实参，所以早退必须是显式 `return`，写成 `f(need(), costly())` 等于没早退，
 *    这一条把悬浮态开销从 1.98% 压回与基线不可区分的 0.73%）。
 *
 * 本模块刻意不 import electron，也不 import dock.ts 之外的任何窗口知识：三件事经 `source` 注入。
 */
import { needsZReassert } from './dock';

/** 轮询间隔 = 用户能看到的闪烁时长。200ms 时"点任务栏闪一下"就是守卫在捞它。 */
export const Z_GUARD_INTERVAL_MS = 30;
/** 行内判定要解析显示器，比遮挡判定贵一个量级，单独按 300ms 刷（30ms × 10 拍）。 */
export const Z_ROW_REFRESH_TICKS = 10;

/**
 * 守卫要问的三件事。快慢分明，所以是三个入口而不是一个「拍一次状态」：
 * `covered` 每拍都问，必须便宜；`refresh` 只在慢刷新那一拍问。
 * `T` 是任务栏根窗口句柄的类型，对守卫透明（真实现里是 `bigint | null`）。
 */
export interface TaskbarSource<T> {
  /** 窗口还活着才值得问任何一件事（销毁后的定时器是纯空转） */
  active(): boolean;
  /** 慢刷新那一拍：解析显示器判**行内** + 重取任务栏句柄 */
  refresh(): { inTaskbarRow: boolean; taskbar: T };
  /** 每拍：卡片中心那一像素是否已被任务栏占住，即小窗被压到了任务栏下面 */
  covered(taskbar: T | null): boolean;
}

export interface ZOrderGuardOptions<T> {
  source: TaskbarSource<T>;
  /** 重申 z-order 档位。守卫只在判据成立时调它，其余时候一毛不拔 */
  reassert(): void;
  /** 起一个周期定时器，返回取消函数。测试在这里拿到 `tick`，于是「拍与拍之间」完全可控 */
  startTimer(tick: () => void, intervalMs: number): () => void;
  intervalMs?: number;
  refreshTicks?: number;
}

export function createZOrderGuard<T>(opts: ZOrderGuardOptions<T>) {
  const intervalMs = opts.intervalMs ?? Z_GUARD_INTERVAL_MS;
  const refreshTicks = opts.refreshTicks ?? Z_ROW_REFRESH_TICKS;
  let cancel: (() => void) | null = null;
  let tick = 0;
  // 慢刷新的缓存：句柄与行内状态都只按 refreshTicks 更新，其余拍直接吃缓存
  let inTaskbarRow = false;
  let taskbar: T | null = null;

  function poll(): void {
    if (!opts.source.active()) return;
    if (tick % refreshTicks === 0) {
      const refreshed = opts.source.refresh();
      inTaskbarRow = refreshed.inTaskbarRow;
      taskbar = refreshed.taskbar;
    }
    tick++;
    // 短路：不在行内时连遮挡判定都不做（见文件头那条实测）
    if (!inTaskbarRow) return;
    if (!needsZReassert(inTaskbarRow, opts.source.covered(taskbar))) return;
    opts.reassert();
  }

  return {
    start(): void {
      if (cancel !== null) return;
      cancel = opts.startTimer(poll, intervalMs);
    },
    stop(): void {
      if (cancel === null) return;
      const c = cancel;
      cancel = null;
      c();
    },
    get running(): boolean {
      return cancel !== null;
    }
  };
}
