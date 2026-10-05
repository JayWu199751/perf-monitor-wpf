# 网速只统计物理网卡，并按真实差分窗口摊平

> 状态：Accepted。选网卡的口径在 `electron/src/main/sources/network.ts`（`selectNetRows`），速率的窗口换算在同文件的 `createNetRateTracker`，两者由 `electron/tests/network.test.ts` 覆盖。术语见 `CONTEXT.md` 的**物理网卡口径**。

## 问题

小窗的下载速度长期偏高。本机（装了 mihomo，TUN 模式）实测：用 curl 拉一个 115 MB 的文件，应用层吞吐 15.66 MB/s，物理网卡 WLAN 及其 5 个 filter 子接口都记 15.42 MB/s，而 mihomo 的 `Meta Tunnel` 记 **47.38 MB/s**——同一份流量被虚拟网卡重复计入，约 3 倍。旧的选法是"在所有 up 网卡里取收发之和最大的那块"，正好每一轮都挑中它。

## 决策

- **候选集先收窄到物理网卡**：判据 `MIB_IF_ROW2.PhysicalMediumType != NdisPhysicalMediumUnspecified`，与 `Get-NetAdapter -Physical` 同口径。本机两者给出的都是 Realtek 有线 + MediaTek WLAN 两块，mihomo 与 Hyper-V 默认交换机一并排除。
- **收窄之后仍取最大，不求和**：同一块物理网卡在本机有 6 行记录（母卡 + 5 个 filter 子接口），同 MAC、同字节计数，求和会翻 6 倍。
- **整机一块物理网卡都没有时退回全集**：驱动不填 `PhysicalMediumType` 的机器上，宁可读数偏大，也不要永远显示 0。
- **速率按真实差分窗口摊平**，不按标称刷新周期：换算用两次采样的时间戳差。poller 的节奏是"睡 `refresh_fast_ms` + 本轮耗时"，且该间隔用户可调，把窗口钉死成标称值就等于每轮都按偏短的时间换算，读数系统性偏高。

## Considered Options

- **所有 up 网卡取最大（旧行为）**：被 TUN 网卡虚报 3 倍，本机必现。放弃。
- **求和所有网卡**：filter 子接口共享同一份计数，直接翻若干倍。放弃。
- **用 `InterfaceAndOperStatusFlags` 的 `LogicalAdapter` 位**：本机 mihomo 该位是 0，反而是 WLAN 那 5 个 filter 子接口为 1——挡不住要挡的那个。放弃。
- **照抄常见网卡库的通用排除规则**（链路速率为 0 / 未连接 / `PhysicalAddressLength == 0` 三者占其一即排除）：能挡住 mihomo（TUN 无 MAC），但挡不住 Hyper-V 默认交换机（有 MAC、报 10G）。放弃。
- **按默认路由选网卡**：最贴近"我实际走的那块网卡"，但要引入路由查询、策略路由与多网关的复杂度，对自用工具不划算。暂不采用。
- **本方案（采用）**：判据是 OS 自己就在用的那一条。代价是极少数不填 `PhysicalMediumType` 的物理网卡会被漏掉，由"退回全集"兜底——但在多网卡机器上这可能让读数偏小。

## 复验方法

单测锁住的是算术，锁不住"这台机器的虚拟网卡到底虚报多少"。要重跑真机对表（本地调试目录，不入库）：

```powershell
cd electron
npx esbuild .netdiag/entry.ts --bundle --platform=node --format=esm --external:koffi --outfile=.netdiag/bundle.mjs
node .netdiag/dump.mjs     # 逐网卡比对 koffi 读到的累计字节与 Get-NetAdapterStatistics，验 MIB_IF_ROW2 偏移
node .netdiag/verify.mjs   # curl 拉 115MB，把小窗读数与真实吞吐对表；修复前 FAIL（报 47，实际 15.7）
```
