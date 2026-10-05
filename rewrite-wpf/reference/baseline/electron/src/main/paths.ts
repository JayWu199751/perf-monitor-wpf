import { app } from 'electron';
import { existsSync } from 'node:fs';
import { join } from 'node:path';

// 图标等静态资源在仓库根的 resources/，electron/ 下不另存副本。
// 打包时经 electron-builder 的 extraResources 落到 <resources>/resources/，
// 因此两种环境下的基准目录不同，统一在这里解析。
function resourceRoots(): string[] {
  if (app.isPackaged) return [join(process.resourcesPath, 'resources')];
  return [
    // electron-vite 的 dev 下 __dirname 在 out/main，仓库根 resources 需上溯三级
    join(app.getAppPath(), '..', 'resources'),
    join(app.getAppPath(), 'resources'),
    join(process.cwd(), 'resources')
  ];
}

// 返回首个真实存在的同名资源路径；全部落空时返回最后一个候选，
// 让调用方的 isEmpty() 兜底逻辑去处理，而不是在这里抛错打断启动。
export function resourcePath(fileName: string): string {
  const roots = resourceRoots();
  for (const dir of roots) {
    const candidate = join(dir, fileName);
    if (existsSync(candidate)) return candidate;
  }
  return join(roots[roots.length - 1] ?? '', fileName);
}
