import { describe, expect, it } from 'vitest';
import { readInterfaceRows } from '../src/main/netif';
import { createNetRateTracker, readNetwork, selectNetRows } from '../src/main/sources/network';

const MIB = 1024 * 1024;
// 默认按物理网卡造数据；虚拟网卡（TUN/虚拟交换机）用 virtual()。
// 字节数允许带小数（如 15.4 MiB），落到整数字节上。
const row = (index: number, inOctets: number, outOctets: number) => ({
  index,
  physical: true,
  inOctets: BigInt(Math.round(inOctets * MIB)),
  outOctets: BigInt(Math.round(outOctets * MIB))
});
const virtual = (index: number, inOctets: number, outOctets: number) => ({
  ...row(index, inOctets, outOctets),
  physical: false
});

describe('network rate tracker', () => {
  it('只登记基线，首轮报 0', () => {
    const tracker = createNetRateTracker();
    expect(tracker.sample([row(1, 100, 20)], 0)).toEqual({ downMBs: 0, upMBs: 0 });
  });

  it('按时间窗口算速率，并取收发之和最大的那块网卡', () => {
    const tracker = createNetRateTracker();
    tracker.sample([row(1, 0, 0), row(2, 0, 0)], 0);

    // 网卡 1 走 10 MiB/s，网卡 2 合计只有 2 MiB/s
    const sample = tracker.sample([row(1, 10, 0), row(2, 1, 1)], 1000);
    expect(sample).toEqual({ downMBs: 10, upMBs: 0 });
  });

  it('计数器变小按 0 处理，不报出天文数字', () => {
    const tracker = createNetRateTracker();
    tracker.sample([row(1, 500, 500)], 0);

    expect(tracker.sample([row(1, 10, 501)], 1000)).toEqual({ downMBs: 0, upMBs: 1 });
  });

  it('新出现的网卡没有基线，本轮不参与', () => {
    const tracker = createNetRateTracker();
    tracker.sample([row(1, 0, 0)], 0);

    expect(tracker.sample([row(1, 1, 0), row(9, 999, 999)], 1000)).toEqual({ downMBs: 1, upMBs: 0 });
  });

  it('小窗隐藏停摆后按真实间隔摊平，不按标称周期虚报', () => {
    const tracker = createNetRateTracker();
    tracker.sample([row(1, 0, 0)], 0);

    // 停摆 5 秒期间共走了 5 MiB，摊下来是 1 MiB/s
    expect(tracker.sample([row(1, 5, 0)], 5000)).toEqual({ downMBs: 1, upMBs: 0 });
  });

  it('reset 之后重新攒基线', () => {
    const tracker = createNetRateTracker();
    tracker.sample([row(1, 0, 0)], 0);
    tracker.sample([row(1, 8, 0)], 1000);
    tracker.reset();

    expect(tracker.sample([row(1, 100, 0)], 2000)).toEqual({ downMBs: 0, upMBs: 0 });
  });

  // 回归：mihomo 的 Meta Tunnel 把同一份流量重复计入，本机实测 15.4 MB/s 的下载
  // 在它身上记成 47.4 MB/s。旧的"取收发之和最大"选法正好挑中它，小窗因此虚报约 3 倍。
  it('虚拟网卡计数虚高时不选它，只报物理网卡的实际速率', () => {
    const tracker = createNetRateTracker();
    tracker.sample([row(3, 0, 0), virtual(16, 0, 0)], 0);

    const sample = tracker.sample([row(3, 15.4, 0.2), virtual(16, 47.4, 1.5)], 1000);
    expect(sample).toEqual({ downMBs: 15.4, upMBs: 0.2 });
  });

  it('同一块物理网卡的 filter 子接口共享计数，取最大而不是求和', () => {
    const tracker = createNetRateTracker();
    // 本机 WLAN 有 1 块母卡 + 5 个同 MAC 同计数的 filter 子接口
    const dups = [3, 11, 13, 22, 25, 29].map((i) => row(i, 0, 0));
    tracker.sample(dups, 0);

    const moved = [3, 11, 13, 22, 25, 29].map((i) => row(i, 15.4, 0.2));
    expect(tracker.sample(moved, 1000)).toEqual({ downMBs: 15.4, upMBs: 0.2 });
  });

  it('整机没有物理网卡时退回全集，不永远报 0', () => {
    const tracker = createNetRateTracker();
    tracker.sample([virtual(16, 0, 0)], 0);

    expect(tracker.sample([virtual(16, 3, 0)], 1000)).toEqual({ downMBs: 3, upMBs: 0 });
  });

  it('基线之后才上线的虚拟网卡再忙也不参与', () => {
    const tracker = createNetRateTracker();
    tracker.sample([row(3, 0, 0), virtual(16, 0, 0)], 0);

    // WLAN 走了 2 MiB；虚拟网卡走了 99 MiB，被候选集挡在外面
    expect(tracker.sample([row(3, 2, 0), virtual(16, 99, 0)], 1000)).toEqual({
      downMBs: 2,
      upMBs: 0
    });
  });
});

describe('selectNetRows', () => {
  it('只留物理网卡', () => {
    expect(selectNetRows([row(3, 0, 0), virtual(16, 0, 0)])).toEqual([row(3, 0, 0)]);
  });

  it('全是虚拟网卡时原样返回', () => {
    const rows = [virtual(16, 0, 0), virtual(34, 0, 0)];
    expect(selectNetRows(rows)).toEqual(rows);
  });

  it('空集合仍是空集合', () => {
    expect(selectNetRows([])).toEqual([]);
  });
});

// 读表那一步只有 Windows 跑得起来；这条集成测试盯的是 MIB_IF_ROW2 的内存布局，
// 字段偏移一旦算错，轻则读出垃圾速率，重则像 GetIfTable2 那样直接 AV。
const windows = process.platform === 'win32' ? describe : describe.skip;

windows('iphlpapi integration', () => {
  it('枚举到处于 up 状态的非回环网卡', () => {
    const rows = readInterfaceRows();
    expect(rows.length).toBeGreaterThan(0);
    expect(rows.some((r) => r.up && r.index > 1)).toBe(true);
  });

  // PhysicalMediumType 的偏移一旦读错，要么整机没有一块网卡被认成物理网卡（网速恒为 0），
  // 要么虚拟网卡混进候选集（网速虚报）。两个方向都在这里变红。
  it('真实机器上至少有一块 up 的物理网卡，且候选集非空', () => {
    const rows = readInterfaceRows().filter((r) => r.up && r.index > 1);
    expect(rows.some((r) => r.physical)).toBe(true);
    expect(selectNetRows(rows).length).toBeGreaterThan(0);
  });

  it('两次采样能算出有限的收发速率', async () => {
    readNetwork();
    await new Promise((resolve) => setTimeout(resolve, 1200));
    const sample = readNetwork();

    expect(Number.isFinite(sample.downMBs)).toBe(true);
    expect(Number.isFinite(sample.upMBs)).toBe(true);
    expect(sample.downMBs).toBeGreaterThanOrEqual(0);
    expect(sample.upMBs).toBeGreaterThanOrEqual(0);
  });
});
