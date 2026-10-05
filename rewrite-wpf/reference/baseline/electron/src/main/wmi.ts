import koffi from 'koffi';

// 这里只保留 WMI 所需的 COM 边界。WMI 接口是 COM vtable，没有可直接按函数名
// 导出的 IWbemServices 方法，因此通过对象的 vtable 指针调用方法。
const IS_WINDOWS = process.platform === 'win32';

const CLSCTX_INPROC_SERVER = 0x1;
const COINIT_MULTITHREADED = 0x0;
const COINIT_APARTMENTTHREADED = 0x2;
const RPC_E_CHANGED_MODE = -2147417850;
const RPC_C_AUTHN_WINNT = 10;
const RPC_C_AUTHZ_NONE = 0;
const RPC_C_AUTHN_LEVEL_CALL = 3;
const RPC_C_IMP_LEVEL_IMPERSONATE = 3;
const EOAC_NONE = 0;
const WBEM_FLAG_FORWARD_ONLY = 0x20;
const WBEM_FLAG_RETURN_IMMEDIATELY = 0x10;
const WBEM_INFINITE = -1;

// {4590F811-1D3A-11D0-891F-00AA004B2E24}
const CLSID_WBEM_LOCATOR = Buffer.from([
  0x11, 0xf8, 0x90, 0x45, 0x3a, 0x1d, 0xd0, 0x11,
  0x89, 0x1f, 0x00, 0xaa, 0x00, 0x4b, 0x2e, 0x24
]);

// {DC12A687-737F-11CF-884D-00AA004B2E24}
const IID_IWBEM_LOCATOR = Buffer.from([
  0x87, 0xa6, 0x12, 0xdc, 0x7f, 0x73, 0xcf, 0x11,
  0x88, 0x4d, 0x00, 0xaa, 0x00, 0x4b, 0x2e, 0x24
]);

type ComPointer = bigint;
type KoffiSlot = ReturnType<typeof koffi.alloc>;

// 在非 Windows 环境保留模块可导入性；Electron 的正式目标仍是 Windows。
const ole32 = IS_WINDOWS ? koffi.load('ole32.dll') : null;
const oleaut32 = IS_WINDOWS ? koffi.load('oleaut32.dll') : null;

const coInitializeEx = ole32?.func('__stdcall', 'CoInitializeEx', 'int32_t', ['void*', 'uint32_t']);
const coUninitialize = ole32?.func('__stdcall', 'CoUninitialize', 'void', []);
const coCreateInstance = ole32?.func('__stdcall', 'CoCreateInstance', 'int32_t', [
  'const void*',
  'void*',
  'uint32_t',
  'const void*',
  '_Out_ void **'
]);
const coSetProxyBlanket = ole32?.func('__stdcall', 'CoSetProxyBlanket', 'int32_t', [
  'void*',
  'uint32_t',
  'uint32_t',
  'void*',
  'uint32_t',
  'uint32_t',
  'void*',
  'uint32_t'
]);
const sysAllocString = oleaut32?.func('__stdcall', 'SysAllocString', 'void*', ['str16']);
const sysFreeString = oleaut32?.func('__stdcall', 'SysFreeString', 'void', ['void*']);
const variantClear = oleaut32?.func('__stdcall', 'VariantClear', 'int32_t', ['void*']);

const releaseMethod = koffi.proto('__stdcall', 'uint32_t', ['void*']);
const outComPointer = koffi.out(koffi.pointer('void*'));
const outUint32 = koffi.out(koffi.pointer('uint32_t'));

// IWbemLocator::ConnectServer（vtable index 3）
const connectServerMethod = koffi.proto('__stdcall', 'int32_t', [
  'void*',
  'void*',
  'void*',
  'void*',
  'void*',
  'int32_t',
  'void*',
  'void*',
  outComPointer
]);

// IWbemServices::ExecQuery（vtable index 20）
const execQueryMethod = koffi.proto('__stdcall', 'int32_t', [
  'void*',
  'void*',
  'void*',
  'int32_t',
  'void*',
  outComPointer
]);

// IEnumWbemClassObject::Next（vtable index 4）
const nextMethod = koffi.proto('__stdcall', 'int32_t', [
  'void*',
  'int32_t',
  'uint32_t',
  outComPointer,
  outUint32
]);

// IWbemClassObject::Get（vtable index 4）
const getMethod = koffi.proto('__stdcall', 'int32_t', [
  'void*',
  'void*',
  'int32_t',
  'void*',
  outUint32,
  outUint32
]);

let comInitialized = false;

function succeeded(hr: number): boolean {
  return hr >= 0;
}

function asVoidPointer(value: ComPointer | null): unknown {
  return koffi.as(value, 'void*');
}

function pointerFromSlot(slot: KoffiSlot): ComPointer | null {
  const value = koffi.decode(slot, 'void*') as ComPointer | null;
  if (typeof value === 'bigint') return value === 0n ? null : value;
  if (typeof value === 'number') return value === 0 ? null : BigInt(value);
  return null;
}

function numberFromSlot(slot: KoffiSlot): number {
  return Number(koffi.decode(slot, 'uint32_t'));
}

function freeSlot(slot: KoffiSlot): void {
  try {
    koffi.free(slot);
  } catch {
    // 输出槽只承载一次调用的临时值，释放失败不应遮蔽 WMI 读取结果。
  }
}

function methodAddress(instance: ComPointer, index: number): ComPointer {
  const vtable = koffi.decode(instance, 'void*') as ComPointer;
  // 不使用 koffi.view：它会把任意 COM vtable 地址包装成外部 ArrayBuffer，
  // Electron 的 V8/N-API 环境下对该地址的检查会触发 native fatal error。
  const methods = koffi.decode(vtable, 'void*', index + 1) as Array<ComPointer | number>;
  const method = methods[index];
  if (typeof method === 'bigint') return method;
  if (typeof method === 'number') return BigInt(method);
  throw new Error(`COM vtable method ${index} is unavailable`);
}

function releaseComObject(instance: ComPointer | null): void {
  if (!instance) return;
  try {
    koffi.call(methodAddress(instance, 2), releaseMethod, asVoidPointer(instance));
  } catch {
    // 清理阶段不能让单个 COM 对象的异常阻止其余对象释放。
  }
}

function withBstr<T>(value: string, fn: (bstr: ComPointer) => T): T | null {
  if (!sysAllocString || !sysFreeString) return null;
  const bstr = sysAllocString(value) as ComPointer | null;
  if (!bstr) return null;
  try {
    return fn(bstr);
  } finally {
    sysFreeString(asVoidPointer(bstr));
  }
}

function ensureComInitialized(): boolean {
  if (comInitialized) return true;
  if (!coInitializeEx) return false;

  let hr = coInitializeEx(null, COINIT_MULTITHREADED);
  if (hr === RPC_E_CHANGED_MODE) {
    // Electron 主线程若已被初始化为 STA，则在原有 apartment 模式下继续使用。
    hr = coInitializeEx(null, COINIT_APARTMENTTHREADED);
  }
  if (!succeeded(hr)) return false;

  comInitialized = true;
  return true;
}

function readVariantInteger(variant: Buffer): number | null {
  // Windows x64 的 VARIANT：vt 位于 0，union 数据位于 8。
  if (variant.length < 12) return null;

  switch (variant.readUInt16LE(0)) {
    case 2: return variant.readInt16LE(8); // VT_I2
    case 3: return variant.readInt32LE(8); // VT_I4
    case 16: return variant.readInt8(8); // VT_I1
    case 17: return variant.readUInt8(8); // VT_UI1
    case 18: return variant.readUInt16LE(8); // VT_UI2
    case 19: return variant.readUInt32LE(8); // VT_UI4（MSAcpi_ThermalZoneTemperature 的声明类型）
    case 20: return Number(variant.readBigInt64LE(8)); // VT_I8
    case 21: return Number(variant.readBigUInt64LE(8)); // VT_UI8
    default: return null;
  }
}

/** 从 VARIANT 里取出 ACPI 热区上报的 0.1K 原值并换算成摄氏温度，越界值按无效处理。 */
function readVariantTemperature(variant: Buffer): number | null {
  const raw = readVariantInteger(variant);
  if (raw === null) return null;

  const celsius = raw / 10 - 273.15;
  if (!Number.isFinite(celsius) || celsius <= 0 || celsius >= 120) return null;
  return Math.round(celsius);
}

function readValues(
  enumerator: ComPointer,
  property: string,
  decode: (variant: Buffer) => number | null
): number[] {
  const values: number[] = [];

  for (;;) {
    const objectOut = koffi.alloc('void*', 1);
    const returned = koffi.alloc('uint32_t', 1);
    let hr: number;
    let object: ComPointer | null = null;
    try {
      hr = koffi.call(
        methodAddress(enumerator, 4),
        nextMethod,
        asVoidPointer(enumerator),
        WBEM_INFINITE,
        1,
        objectOut,
        returned
      ) as number;
      if (numberFromSlot(returned) > 0) object = pointerFromSlot(objectOut);
    } catch {
      return values;
    } finally {
      freeSlot(objectOut);
      freeSlot(returned);
    }

    if (!object) break;

    try {
      const value = withBstr(property, (propertyName) => {
        const variant = Buffer.alloc(24);
        try {
          const getHr = koffi.call(
            methodAddress(object, 4),
            getMethod,
            asVoidPointer(object),
            asVoidPointer(propertyName),
            0,
            variant,
            null,
            null
          ) as number;
          return succeeded(getHr) ? decode(variant) : null;
        } finally {
          if (variantClear) {
            try {
              variantClear(variant);
            } catch {
              // 空 VARIANT 或清理失败都不影响下一条记录。
            }
          }
        }
      });
      if (value !== null) values.push(value);
    } finally {
      releaseComObject(object);
    }

    // 负 HRESULT 表示查询失败；若本轮仍带回对象，先保留已经读到的结果。
    if (!succeeded(hr)) break;
  }

  return values;
}

/**
 * 走完整 COM/WMI 链路读取某一属性的所有取值：CoCreateInstance -> ConnectServer ->
 * ExecQuery -> Next -> Get。权限不足、类不存在或没有结果时返回空数组。
 */
export function readWmiValues(
  namespace: string,
  wql: string,
  property: string,
  decode: (variant: Buffer) => number | null
): number[] {
  const values: number[] = [];
  if (!IS_WINDOWS || !coCreateInstance || !coSetProxyBlanket || !variantClear) return values;
  if (!ensureComInitialized()) return values;

  let locator: ComPointer | null = null;
  let services: ComPointer | null = null;
  let enumerator: ComPointer | null = null;

  try {
    const locatorOut = koffi.alloc('void*', 1);
    let createHr: number;
    try {
      createHr = coCreateInstance(
        CLSID_WBEM_LOCATOR,
        null,
        CLSCTX_INPROC_SERVER,
        IID_IWBEM_LOCATOR,
        locatorOut
      );
      if (succeeded(createHr)) locator = pointerFromSlot(locatorOut);
    } finally {
      freeSlot(locatorOut);
    }
    if (!succeeded(createHr)) return values;
    if (!locator) return values;

    const serviceResult = withBstr(namespace, (namespaceBstr) => {
      const serviceOut = koffi.alloc('void*', 1);
      let hr: number;
      let service: ComPointer | null = null;
      try {
        hr = koffi.call(
          methodAddress(locator as ComPointer, 3),
          connectServerMethod,
          asVoidPointer(locator),
          asVoidPointer(namespaceBstr),
          asVoidPointer(null),
          asVoidPointer(null),
          asVoidPointer(null),
          0,
          asVoidPointer(null),
          asVoidPointer(null),
          serviceOut
        ) as number;
        if (succeeded(hr)) service = pointerFromSlot(serviceOut);
      } finally {
        freeSlot(serviceOut);
      }
      if (!succeeded(hr)) return null;
      return service;
    });
    services = serviceResult;
    if (!services) return values;

    const blanketHr = coSetProxyBlanket(
      asVoidPointer(services),
      RPC_C_AUTHN_WINNT,
      RPC_C_AUTHZ_NONE,
      asVoidPointer(null),
      RPC_C_AUTHN_LEVEL_CALL,
      RPC_C_IMP_LEVEL_IMPERSONATE,
      asVoidPointer(null),
      EOAC_NONE
    );
    if (!succeeded(blanketHr)) return values;

    const queryResult = withBstr('WQL', (queryLanguage) => withBstr(
      wql,
      (query) => {
        const enumeratorOut = koffi.alloc('void*', 1);
        let hr: number;
        let result: ComPointer | null = null;
        try {
          hr = koffi.call(
            methodAddress(services as ComPointer, 20),
            execQueryMethod,
            asVoidPointer(services),
            asVoidPointer(queryLanguage),
            asVoidPointer(query),
            WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
            asVoidPointer(null),
            enumeratorOut
          ) as number;
          if (succeeded(hr)) result = pointerFromSlot(enumeratorOut);
        } finally {
          freeSlot(enumeratorOut);
        }
        if (!succeeded(hr)) return null;
        return result;
      }
    ));
    enumerator = queryResult;
    if (!enumerator) return values;

    return readValues(enumerator, property, decode);
  } catch {
    return values;
  } finally {
    releaseComObject(enumerator);
    releaseComObject(services);
    releaseComObject(locator);
  }
}

/**
 * 直接从 WMI root\\WMI 查询 ACPI 热区温度，不启动 PowerShell。
 * 非管理员、没有 ACPI 热区或 WMI 查询失败时返回 null。
 */
export function readCpuTemperature(): number | null {
  const zones = readWmiValues(
    'root\\WMI',
    'SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature',
    'CurrentTemperature',
    readVariantTemperature
  );
  if (zones.length === 0) return null;

  return zones.reduce((best, value) => Math.max(best, value));
}

/** 在应用退出时释放本模块持有的 COM 初始化引用。 */
export function disposeWmi(): void {
  if (!comInitialized) return;
  try {
    coUninitialize?.();
  } finally {
    comInitialized = false;
  }
}

// 仅供单元测试验证 VARIANT 解码与整条 COM 链路，不暴露 COM 对象或原始 FFI。
export const decodeVariantInteger = readVariantInteger;
export const decodeCurrentTemperature = readVariantTemperature;
