import { describe, expect, it, vi } from 'vitest';
import { buildRelaunchCommand, isElevated } from '../src/main/elevate';

describe('buildRelaunchCommand', () => {
  it('quotes each argument so paths with spaces survive PowerShell parsing', () => {
    const cmd = buildRelaunchCommand(
      'C:\\Program Files\\性能小窗\\性能小窗.exe',
      ['--elevated'],
      'C:\\Users\\jay'
    );
    expect(cmd).toContain('Start-Process');
    expect(cmd).toContain('"C:\\Program Files\\性能小窗\\性能小窗.exe"');
    expect(cmd).toContain('"--elevated"');
    expect(cmd).toContain('-Verb RunAs');
  });

  it('escapes the PowerShell backtick, dollar and double quote', () => {
    const cmd = buildRelaunchCommand('C:\\a`b$c"d.exe', [], 'C:\\');
    expect(cmd).toContain('a``b`$c`"d.exe');
  });
});

describe('isElevated', () => {
  it('defers to the injected probe', () => {
    expect(isElevated(() => true)).toBe(true);
    expect(isElevated(() => false)).toBe(false);
  });

  it('never calls the probe off Windows', () => {
    const original = process.platform;
    Object.defineProperty(process, 'platform', { value: 'darwin', configurable: true });
    const probe = vi.fn(() => false);
    try {
      expect(isElevated(probe)).toBe(true);
      expect(probe).not.toHaveBeenCalled();
    } finally {
      Object.defineProperty(process, 'platform', { value: original, configurable: true });
    }
  });
});
