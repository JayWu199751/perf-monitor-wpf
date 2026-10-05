import { screen } from 'electron';
import type { BrowserWindow } from 'electron';
import type { Rect } from './geometry';
import { cardInTaskbarRow } from './dock';
import { findTaskbarWindow, isPointOwnedByWindow } from './win32';
import type { WidgetWindowLike, WidgetWindowPorts } from './widgetWindow';

// 全部窗口能力只在这里映射到 Electron / FFI，落位与结算规则归 controller。
export function createWidgetWindowPorts(raw: WidgetWindowLike): WidgetWindowPorts {
  const win = raw as unknown as BrowserWindow;
  const displayFor = (b: Rect): Electron.Display => screen.getDisplayNearestPoint({
    x: Math.round(b.x + b.width / 2), y: Math.round(b.y + b.height / 2)
  });
  return {
    docking: {
      getBounds: () => win.getBounds(),
      displayFor,
      setBounds: (bounds) => win.setBounds(bounds)
    },
    taskbar: {
      active: () => !win.isDestroyed(),
      // 只在慢刷新那一拍解析显示器与查句柄，维持 ADR-0005 的热路径成本。
      refresh: () => {
        const b = win.getBounds();
        return { inTaskbarRow: cardInTaskbarRow(displayFor(b), b), taskbar: findTaskbarWindow() };
      },
      covered: (taskbar) => {
        if (win.isDestroyed() || !taskbar) return false;
        const b = win.getBounds();
        const c = screen.dipToScreenPoint({
          x: Math.round(b.x + b.width / 2), y: Math.round(b.y + b.height / 2)
        });
        return isPointOwnedByWindow(taskbar, Math.round(c.x), Math.round(c.y));
      }
    },
    displayEvents: {
      onChange: (cb) => {
        screen.on('display-metrics-changed', cb);
        screen.on('display-added', cb);
        screen.on('display-removed', cb);
        return () => {
          screen.removeListener('display-metrics-changed', cb);
          screen.removeListener('display-added', cb);
          screen.removeListener('display-removed', cb);
        };
      }
    }
  };
}
