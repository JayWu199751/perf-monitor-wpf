// 测量夹具用的假 preload：只提供渲染层依赖的 window.api 门面，数据来自
// 同目录 .fixture.json（由 measure.cjs 每轮重写）。不参与打包（build.files
// 只含 out/** + package.json），是纯调试装置。
const fs = require('node:fs');
const path = require('node:path');

const FIXTURE = path.join(__dirname, '.fixture.json');

// 冻结页内时钟：不冻的话小窗时间段的文字每秒都在变，量具自身就不可复现，
// 「两次跑结果必须一致」这条 Phase 1 判据也无从验证。
const FROZEN = Date.parse('2025-01-01T12:34:56');
class FrozenDate extends Date {
  constructor(...args) {
    if (args.length === 0) super(FROZEN);
    else super(...args);
  }
  static now() {
    return FROZEN;
  }
}
window.Date = FrozenDate;

function read() {
  return JSON.parse(fs.readFileSync(FIXTURE, 'utf8'));
}

window.api = {
  async getSettings() {
    return read().settings;
  },
  async setSettings(patch) {
    return { ...read().settings, ...patch };
  },
  onSettings() {
    return () => {};
  },
  onMetrics(cb) {
    cb(read().metrics);
    return () => {};
  },
  resizeWidget() {},
};
