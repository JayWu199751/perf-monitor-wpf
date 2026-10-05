import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { app } from 'electron';
import { DEFAULT_SETTINGS, fillDefaultSettings } from './settings';
import type { Settings } from '../shared/types';

function settingsPath(): string {
  return join(app.getPath('userData'), 'settings.json');
}

export function loadSettings(): Settings {
  try {
    const parsed = JSON.parse(readFileSync(settingsPath(), 'utf8')) as Partial<Settings>;
    return fillDefaultSettings(parsed);
  } catch {
    return structuredClone(DEFAULT_SETTINGS);
  }
}

export function saveSettings(s: Settings): void {
  try {
    mkdirSync(app.getPath('userData'), { recursive: true });
    writeFileSync(settingsPath(), JSON.stringify(s, null, 2), 'utf8');
  } catch (err) {
    console.error('saveSettings failed', err);
  }
}
