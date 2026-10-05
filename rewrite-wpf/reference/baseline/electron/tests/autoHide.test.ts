import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createAutoHideController } from '../src/main/autoHide';

function harness(initialVisible = true) {
  let visible = initialVisible;
  const focus = vi.fn();
  const controller = createAutoHideController({
    isVisible: () => visible,
    hide: () => {
      visible = false;
    },
    show: () => {
      visible = true;
    },
    focus
  });
  return { controller, isVisible: () => visible, focus };
}

describe('autoHide controller', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('restores the widget when fullscreen ends', () => {
    const h = harness();
    h.controller.onFullscreenChange(true);
    expect(h.isVisible()).toBe(false);
    h.controller.onFullscreenChange(false);
    expect(h.isVisible()).toBe(true);
  });

  it('keeps a manually hidden widget hidden across a fullscreen cycle', () => {
    const h = harness();
    h.controller.onManualToggle(); // 用户手动隐藏
    expect(h.isVisible()).toBe(false);
    h.controller.onFullscreenChange(true);
    h.controller.onFullscreenChange(false);
    expect(h.isVisible()).toBe(false);
  });

  it('does not steal focus back when a manual show ends fullscreen', () => {
    const h = harness(false);
    h.controller.onFullscreenChange(true); // 已隐藏，无动作
    h.controller.onManualShow(); // 用户手动显示
    expect(h.isVisible()).toBe(true);
    h.controller.onFullscreenChange(false);
    // autoHidden 已被 onManualShow 清除，不会重复 show/focus
    expect(h.focus).toHaveBeenCalledTimes(1);
  });

  it('treats manual toggle as the priority state', () => {
    const h = harness();
    h.controller.onFullscreenChange(true);
    h.controller.onManualToggle(); // 全屏中用户手动显示
    expect(h.isVisible()).toBe(true);
    h.controller.onFullscreenChange(false);
    expect(h.isVisible()).toBe(true);
  });
});
