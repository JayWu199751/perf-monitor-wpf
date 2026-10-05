// 设置窗生命周期与尺寸策略：懒创建 -> 关闭即隐藏 -> 隐藏够久再销毁，回收整个渲染进程。
//
// 为什么不是"只隐藏"：隐藏的设置窗仍占一个渲染进程（本机实测 37MB 私有内存），
// 而设置窗是极低频入口——开一次改完就再也不碰，那份内存就一直挂到退出。
// 只隐藏换的是"再次打开零延迟"，所以销毁要等闲置够久，常用路径不受影响。
//
// 本模块刻意不 import electron：窗口经窄接口注入、定时器经 schedule 注入，
// 因此整条策略可单测（与 autoHide.ts 同一手法）。

export interface SettingsWindowLike {
  show(): void;
  focus(): void;
  hide(): void;
  destroy(): void;
  isDestroyed(): boolean;
  on(event: 'close', handler: (e: { preventDefault(): void }) => void): void;
  on(event: 'closed', handler: () => void): void;
}

export interface SettingsWindowOptions {
  createWindow: () => SettingsWindowLike;
  // 退出流程中 close 必须放行，否则 app.quit() 被"关闭即隐藏"中止（ADR-0001）
  isQuitting: () => boolean;
  // 隐藏多久后销毁；<=0 退化为旧的"只隐藏、永不销毁"
  idleDestroyMs: number;
  // 返回取消函数
  schedule: (fn: () => void, ms: number) => () => void;
}

export function createSettingsWindowController(opts: SettingsWindowOptions) {
  let win: SettingsWindowLike | null = null;
  let cancelIdle: (() => void) | null = null;

  function clearIdle(): void {
    if (!cancelIdle) return;
    const cancel = cancelIdle;
    cancelIdle = null;
    cancel();
  }

  function ensureWindow(): SettingsWindowLike {
    if (win && !win.isDestroyed()) return win;
    const created = opts.createWindow();

    created.on('close', (e) => {
      if (opts.isQuitting()) return;
      e.preventDefault();
      created.hide();
      clearIdle();
      if (opts.idleDestroyMs > 0) {
        cancelIdle = opts.schedule(() => {
          cancelIdle = null;
          if (!created.isDestroyed()) created.destroy();
        }, opts.idleDestroyMs);
      }
    });

    created.on('closed', () => {
      clearIdle();
      if (win === created) win = null;
    });

    win = created;
    return created;
  }

  return {
    open(): void {
      // 闲置期内重新打开：先撤掉待销毁，命中热窗口就是纯 show
      clearIdle();
      const w = ensureWindow();
      w.show();
      w.focus();
    },
    // 退出时调用：撤掉待销毁定时器并直接销毁，销毁走 destroy 不再触发 close
    dispose(): void {
      clearIdle();
      if (win && !win.isDestroyed()) win.destroy();
      win = null;
    },
    get hasWindow(): boolean {
      return win !== null && !win.isDestroyed();
    },
    get destroyPending(): boolean {
      return cancelIdle !== null;
    }
  };
}

export type SettingsWindowController = ReturnType<typeof createSettingsWindowController>;

// —— 内容高 ——
// 设置窗的内容是死的（四张分组卡片 + 头 + 一行状态），所以高度按实测值给：
// 2026-09-30 实测整页 850 CSS px（卡片 243.3 / 132.9 / 169.7 / 132.9，行高 36.8，头部 66）。
// 旧值 680 是照「~660 的内容」写的，之后的字号/行高调整把它甩下了 170px——
// 表现是常驻滚动条 + 「行为」整组永远在首屏外，而窗口不可缩放，用户只能滚。
export const SETTINGS_CONTENT_HEIGHT = 880;
// 工作区不够高时的下限：真装不下就交给滚动条，也别压成一条缝。
export const SETTINGS_MIN_HEIGHT = 560;
// 窗口贴到工作区上下边时留的呼吸，顺带躲开 Windows 把窗口顶到任务栏上沿。
export const SETTINGS_WORKAREA_SLACK = 48;

/**
 * 工作区放得下就按内容高开，放不下才退到「工作区高 − 余量」并由滚动条兜底。
 * 高度只在这里算：窗口 resizable:false，换机器、换缩放比都得跟着走。
 */
export function settingsWindowHeight(workAreaHeight: number): number {
  const room = workAreaHeight - SETTINGS_WORKAREA_SLACK;
  return Math.max(SETTINGS_MIN_HEIGHT, Math.min(SETTINGS_CONTENT_HEIGHT, room));
}

// 隐藏后 5 分钟销毁。设置窗渲染进程实测占 ~37MB 私有内存，而"打开设置改一项就收工"
// 之后它基本再不会被碰；5 分钟足够覆盖改完又回来补一眼的连续操作。
export const SETTINGS_IDLE_DESTROY_MS = 5 * 60 * 1000;
