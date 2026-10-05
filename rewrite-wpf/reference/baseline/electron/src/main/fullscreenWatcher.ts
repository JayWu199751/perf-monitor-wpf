import { isWindowFullscreen, type Edges } from './fullscreen';
import type { ForegroundInfo } from './foreground';

export interface FullscreenWatcherOptions {
  getForegroundInfo: () => ForegroundInfo | null;
  onFullscreenChange: (fullscreen: boolean) => void;
}

// 轮询前台窗口判定全屏：Windows 没有可靠的全局"进入全屏"事件，
// 1s 轮询一次前台窗口矩形是自用工具下成本最低且够用的做法。
export function createFullscreenWatcher(opts: FullscreenWatcherOptions) {
  let running = false;
  let timer: ReturnType<typeof setInterval> | null = null;
  let fullscreen = false;

  function check(): void {
    const info = opts.getForegroundInfo();
    const isFull =
      info !== null &&
      isWindowFullscreen(
        info.rect as Edges,
        info.bounds as Edges,
        info.scaleFactor,
        info.style
      );
    if (isFull !== fullscreen) {
      fullscreen = isFull;
      opts.onFullscreenChange(isFull);
    }
  }

  return {
    start(): void {
      if (running) return;
      running = true;
      check();
      timer = setInterval(check, 1000);
    },
    stop(): void {
      running = false;
      if (timer) clearInterval(timer);
      timer = null;
    },
    isFullscreen(): boolean {
      return fullscreen;
    }
  };
}
