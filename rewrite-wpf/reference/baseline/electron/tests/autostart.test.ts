import { describe, expect, it } from 'vitest';
import {
  AUTOSTART_TASK_NAME,
  buildDisableArgs,
  buildEnableArgs,
  buildRunKeyCleanupCommand
} from '../src/main/autostart';
import { ELEVATED_FLAG } from '../src/shared/cli';

describe('buildEnableArgs', () => {
  it('creates a highest-privilege on-logon task with a quoted executable path', () => {
    const args = buildEnableArgs('C:\\Program Files\\性能小窗\\性能小窗.exe');
    expect(args).toContain('/SC');
    expect(args).toContain('ONLOGON');
    expect(args).toContain('/RL');
    expect(args).toContain('HIGHEST');
    expect(args[args.indexOf('/TN') + 1]).toBe(AUTOSTART_TASK_NAME);
    // 带空格路径必须整体加引号，且带上 --elevated 以免登录时再弹 UAC
    expect(args[args.indexOf('/TR') + 1]).toBe(
      `"C:\\Program Files\\性能小窗\\性能小窗.exe" ${ELEVATED_FLAG}`
    );
  });

  it('uses an ASCII task name so schtasks output stays parseable', () => {
    expect(AUTOSTART_TASK_NAME).toMatch(/^[\x20-\x7E]+$/);
  });
});

describe('buildDisableArgs', () => {
  it('deletes the same task it creates', () => {
    expect(buildDisableArgs()).toEqual(['/Delete', '/TN', AUTOSTART_TASK_NAME, '/F']);
  });
});

describe('buildRunKeyCleanupCommand', () => {
  it('matches legacy entries by executable path rather than value name', () => {
    const cmd = buildRunKeyCleanupCommand('C:\\Apps\\Perf.exe');
    expect(cmd).toContain("$exe = 'C:\\Apps\\Perf.exe'");
    expect(cmd).toContain('StartsWith($exe');
    expect(cmd).toContain('Remove-ItemProperty');
  });

  it('doubles single quotes to stay inside a PS single-quoted literal', () => {
    const cmd = buildRunKeyCleanupCommand("C:\\It's here\\Perf.exe");
    expect(cmd).toContain("$exe = 'C:\\It''s here\\Perf.exe'");
  });
});
