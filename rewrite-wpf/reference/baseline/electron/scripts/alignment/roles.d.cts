// roles.cjs 的类型面。量具与反解器都是 .cjs（跑在 electron/node 直下，不过 vite），
// 只有 tests/opticalCenter.test.ts 会把这份分类逻辑当被测对象 import 进来，
// 所以声明写在这里而不是塞进 src/。
export declare const DEGREE: string;
export declare const SUPERSCRIPT: Set<string>;
export declare function kindOf(key: string): string;
export declare function textOf(key: string): string;
export declare function roleOf(key: string): string;
export declare function isSuperscript(key: string): boolean;
