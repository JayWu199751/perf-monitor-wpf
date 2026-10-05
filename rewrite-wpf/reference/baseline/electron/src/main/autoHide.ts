export interface AutoHideOptions {
  isVisible: () => boolean;
  hide: () => void;
  show: () => void;
  focus: () => void;
}

// 编排"其他应用全屏时隐藏小窗"：区分自动隐藏与手动隐藏两种原因，
// 手动状态优先——用户手动隐藏后全屏不再抢跑，手动显示后退出全屏不再抢回。
export function createAutoHideController(opts: AutoHideOptions) {
  let autoHidden = false;
  let manualHidden = false;

  return {
    onFullscreenChange(full: boolean): void {
      if (full) {
        if (!manualHidden && opts.isVisible()) {
          opts.hide();
          autoHidden = true;
        }
      } else {
        if (autoHidden && !manualHidden) {
          opts.show();
        }
        autoHidden = false;
      }
    },
    onManualToggle(): void {
      if (opts.isVisible()) {
        opts.hide();
        manualHidden = true;
        autoHidden = false;
      } else {
        opts.show();
        opts.focus();
        manualHidden = false;
        autoHidden = false;
      }
    },
    // 明确的手动显示入口（如 second-instance 唤起）：显示并清除两种隐藏标志，
    // 保证退出全屏时不会因为 autoHidden 残留而重复 show。
    onManualShow(): void {
      opts.show();
      opts.focus();
      manualHidden = false;
      autoHidden = false;
    }
  };
}
