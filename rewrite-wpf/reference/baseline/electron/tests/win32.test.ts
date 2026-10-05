import { describe, expect, it } from 'vitest';
import { getForegroundWindow, isTokenElevated } from '../src/main/win32';

// 本模块是 koffi FFI 边界，只在 Windows 上有意义；其余平台整组跳过。
const maybe = process.platform === 'win32' ? describe : describe.skip;

maybe('win32 FFI boundary', () => {
  it('reads the process elevation state without throwing', () => {
    expect(typeof isTokenElevated()).toBe('boolean');
  });

  it('keeps returning a stable answer across repeated calls', () => {
    // 每 3 秒的采样轮次都会问一次：句柄若泄漏，这里就会漂移或变 false
    const first = isTokenElevated();
    for (let i = 0; i < 8; i++) expect(isTokenElevated()).toBe(first);
  });

  it('reads the foreground window rect, class and style', () => {
    const fg = getForegroundWindow();
    // 桌面切换瞬间可能没有前台窗口：此时应返回 null 而不是抛错
    if (!fg) return;
    expect(fg.rect.right).toBeGreaterThan(fg.rect.left);
    expect(fg.rect.bottom).toBeGreaterThan(fg.rect.top);
    expect(fg.className.length).toBeGreaterThan(0);
    expect(Number.isInteger(fg.style)).toBe(true);
  });
});
