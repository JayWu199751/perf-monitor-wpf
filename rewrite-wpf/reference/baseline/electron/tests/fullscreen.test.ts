import { describe, expect, it } from 'vitest';
import { WS_CAPTION, WS_THICKFRAME, isSystemWindowClass, isWindowFullscreen } from '../src/main/fullscreen';
import type { Edges } from '../src/main/geometry';

const MONITOR: Edges = { left: 0, top: 0, right: 1920, bottom: 1080 };

const rect = (left: number, top: number, right: number, bottom: number): Edges => ({
  left,
  top,
  right,
  bottom
});

describe('isWindowFullscreen', () => {
  it('accepts a borderless window covering the display', () => {
    expect(isWindowFullscreen(rect(0, 0, 1920, 1080), MONITOR, 1)).toBe(true);
  });

  it('tolerates the 1px shortfall of Chrome F11 fullscreen', () => {
    expect(isWindowFullscreen(rect(0, 0, 1920, 1079), MONITOR, 1)).toBe(true);
    expect(isWindowFullscreen(rect(0, 0, 1920, 1077), MONITOR, 1)).toBe(false);
  });

  it('tries both DIP and physical units because GetWindowRect drifts', () => {
    // 150% 缩放下 Chrome 返回物理像素 rect，与 DIP bounds 比较会失败，需按 scale 再试一次
    const physical = rect(0, 0, 2880, 1620);
    expect(isWindowFullscreen(physical, MONITOR, 1.5)).toBe(true);
    expect(isWindowFullscreen(physical, MONITOR, 1)).toBe(false);
  });

  it('rejects a maximized window that still carries a caption or frame', () => {
    expect(isWindowFullscreen(rect(0, 0, 1920, 1080), MONITOR, 1, WS_CAPTION)).toBe(false);
    expect(isWindowFullscreen(rect(0, 0, 1920, 1080), MONITOR, 1, WS_THICKFRAME)).toBe(false);
  });

  it('rejects a window that only covers part of the display', () => {
    expect(isWindowFullscreen(rect(100, 100, 1200, 800), MONITOR, 1)).toBe(false);
  });
});

describe('isSystemWindowClass', () => {
  it('excludes desktop and taskbar windows that happen to fill the screen', () => {
    for (const cls of ['Progman', 'WorkerW', 'Shell_TrayWnd']) {
      expect(isSystemWindowClass(cls)).toBe(true);
    }
    expect(isSystemWindowClass('Chrome_WidgetWin_1')).toBe(false);
  });
});
