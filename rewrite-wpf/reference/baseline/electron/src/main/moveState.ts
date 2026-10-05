export const WM_ENTERSIZEMOVE = 0x0231;
export const WM_EXITSIZEMOVE = 0x0232;

export interface MoveAwareWindow {
  hookWindowMessage(message: number, callback: () => void): void;
}

// 把平台相关的"窗口正在被原生拖动"信号归一成 onChange 回调。
// 拖动期间的行为（暂停采样、缓冲投递、恢复补采样、不吸附）都由调用方实现。
export function attachMoveState(win: MoveAwareWindow, onChange: (moving: boolean) => void): void {
  if (process.platform === 'win32') {
    // Windows 的 `moved` 只表示某次移动完成，原生移动循环可能仍未退出；
    // 用系统消息精确包住整个拖动区间，避免过早恢复渲染或过早吸附。
    win.hookWindowMessage(WM_ENTERSIZEMOVE, () => onChange(true));
    win.hookWindowMessage(WM_EXITSIZEMOVE, () => onChange(false));
  } else {
    onChange(false);
  }
}
