import { nativeImage } from 'electron';
import { resourcePath } from '../paths';

// 应用与窗口图标（透明底三柱）。打包后由 extraResources 落在 <resources>/resources/。
export function appIconPath(): string {
  return resourcePath('icon.ico');
}

export function appIcon() {
  const img = nativeImage.createFromPath(appIconPath());
  return img.isEmpty() ? undefined : img;
}
