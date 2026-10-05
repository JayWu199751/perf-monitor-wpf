import { afterAll, describe, expect, it } from 'vitest';
import {
  decodeCurrentTemperature,
  decodeVariantInteger,
  disposeWmi,
  readCpuTemperature,
  readWmiValues
} from '../src/main/wmi';

describe('WMI CPU temperature', () => {
  it('reads an integer VARIANT by its type tag', () => {
    const variant = Buffer.alloc(24);
    variant.writeUInt16LE(3, 0); // VT_I4
    variant.writeInt32LE(7, 8);

    expect(decodeVariantInteger(variant)).toBe(7);
  });

  it('decodes a VT_UI4 temperature from tenths of Kelvin', () => {
    const variant = Buffer.alloc(24);
    variant.writeUInt16LE(19, 0); // VT_UI4
    variant.writeUInt32LE(2981, 8); // 24.95 °C

    expect(decodeCurrentTemperature(variant)).toBe(25);
  });

  it('rejects empty or out-of-range VARIANT values', () => {
    expect(decodeCurrentTemperature(Buffer.alloc(8))).toBeNull();

    const variant = Buffer.alloc(24);
    variant.writeUInt16LE(19, 0);
    variant.writeUInt32LE(2731, 8); // 0.0 °C
    expect(decodeCurrentTemperature(variant)).toBeNull();
  });
});

// 需要 Windows 的 COM/WMI 环境；非 Windows CI 仍可运行上面的纯解码测试。
const windows = process.platform === 'win32' ? describe : describe.skip;

windows('Windows WMI integration', () => {
  afterAll(() => disposeWmi());

  // root\cimv2 不需要管理员权限：这条断言真正跑通 CoCreateInstance -> ConnectServer ->
  // ExecQuery -> Next -> Get 全链路，任何 vtable 索引或 koffi 调用约定写错都会在这里红。
  it('reads a real value from root\\cimv2 through the COM chain', () => {
    const values = readWmiValues(
      'root\\cimv2',
      'SELECT NumberOfProcesses FROM Win32_OperatingSystem',
      'NumberOfProcesses',
      decodeVariantInteger
    );

    expect(values[0]).toBeGreaterThan(0);
  });

  it('returns a temperature or null without throwing', () => {
    expect(() => readCpuTemperature()).not.toThrow();
  });

  it('can query repeatedly without leaking COM object state into the next read', () => {
    const first = readCpuTemperature();
    const second = readCpuTemperature();
    expect(second === null || typeof second === 'number').toBe(true);
    expect(first === null || typeof first === 'number').toBe(true);
  });
});
